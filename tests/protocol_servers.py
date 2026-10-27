"""Loopback-only integration fixtures; requires paramiko and pyftpdlib, never shipped in app."""
import argparse, json, os, pathlib, socket, threading, time
import paramiko
from pyftpdlib.authorizers import DummyAuthorizer
from pyftpdlib.handlers import FTPHandler, DTPHandler
from pyftpdlib.servers import FTPServer

parser=argparse.ArgumentParser()
parser.add_argument('--root',required=True)
args=parser.parse_args()
root=pathlib.Path(args.root).resolve(); root.mkdir(parents=True,exist_ok=True)
ftp_root=root/'ftp'; ftp_root.mkdir(exist_ok=True)
sftp_root=root/'sftp'; sftp_root.mkdir(exist_ok=True)
class Auth(paramiko.ServerInterface):
    def check_auth_password(self,user,password): return paramiko.AUTH_SUCCESSFUL if user=='easytest' and password=='easytest-only' else paramiko.AUTH_FAILED
    def get_allowed_auths(self,user): return 'password'
    def check_channel_request(self,kind,channel): return paramiko.OPEN_SUCCEEDED if kind=='session' else paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED
    def check_channel_pty_request(self,*args): return True
    def check_channel_shell_request(self,channel):
        def shell():
            try:
                channel.send(b'SSH_TEST_READY\r\n')
                received=b''
                while data:=channel.recv(4096):
                    received=(received+data)[-64:]
                    if received==b'PORTHARBOR_TEST_EXIT\n':
                        channel.send_exit_status(0); channel.shutdown_write(); channel.close(); return
                    if received==b'PORTHARBOR_TEST_DROP\n':
                        transport=channel.get_transport()
                        transport.sock.shutdown(socket.SHUT_RDWR); transport.close(); return
                    channel.send(b'ECHO:'+data)
            except Exception: pass
        threading.Thread(target=shell,daemon=True).start(); return True
class FaultHandle(paramiko.SFTPHandle):
    def __init__(self,flags,transport,name):
        super().__init__(flags); self.transport=transport; self.name=name
    def drop(self):
        (sftp_root/'faults'/('upload.dropped' if self.name.startswith('drop-upload') else 'download.dropped')).write_text('65536')
        self.transport.sock.shutdown(socket.SHUT_RDWR); self.transport.close()
        return paramiko.SFTP_FAILURE
    def read(self,offset,length):
        if self.name=='drop-download.bin':
            if offset>=65536: return self.drop()
            length=min(length,65536-offset)
        return super().read(offset,length)
    def write(self,offset,data):
        if self.name.startswith('drop-upload.bin.portharbor-') and offset>=65536: return self.drop()
        return super().write(offset,data)
class Sftp(paramiko.SFTPServerInterface):
    commit_faults={'stall-restore','reject-publish','reject-restore','drop-before-backup','drop-after-backup','drop-before-publish','drop-after-publish','drop-after-restore','stall-cleanup'}
    def __init__(self,server,*args,**kwargs):
        super().__init__(server,*args,**kwargs); self.transport=server.transport; self.commit_name=None
    def commit_mark(self,event):
        (sftp_root/'faults'/(self.commit_name+'.'+event)).write_text(event)
    def commit_drop(self):
        self.commit_mark('disconnected')
        self.transport.sock.shutdown(socket.SHUT_RDWR); self.transport.close()
        return paramiko.SFTP_FAILURE
    def commit_stall(self,event):
        self.commit_mark(event)
        # Keep the request unanswered until the client closes this connection.
        # No mutation may run later after a cancelled request.
        while self.transport.is_active(): time.sleep(.02)
        return paramiko.SFTP_FAILURE
    def path(self,path):
        result=(sftp_root/path.lstrip('/')).resolve()
        if not result.is_relative_to(sftp_root): raise PermissionError('outside fixture')
        return result
    def list_folder(self,path):
        try:
            output=[]
            for p in self.path(path).iterdir():
                attr=paramiko.SFTPAttributes.from_stat(p.stat()); attr.filename=p.name; output.append(attr)
            return output
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
    def stat(self,path):
        try: return paramiko.SFTPAttributes.from_stat(self.path(path).stat())
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
    lstat=stat
    def open(self,path,flags,attr):
        try:
            fd=os.open(self.path(path),flags,0o600)
            mode='r+b' if flags & os.O_RDWR else 'wb' if flags & os.O_WRONLY else 'rb'
            f=os.fdopen(fd,mode); handle=FaultHandle(flags,self.transport,pathlib.PurePosixPath(path).name); handle.readfile=handle.writefile=f; return handle
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
    def mkdir(self,path,attr):
        try: self.path(path).mkdir(); return paramiko.SFTP_OK
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
    def rmdir(self,path):
        try: self.path(path).rmdir(); return paramiko.SFTP_OK
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
    def remove(self,path):
        try:
            if self.commit_name=='commit-stall-cleanup.bin' and path.endswith('.bak'):
                return self.commit_stall('cleanup-stalled')
            self.path(path).unlink(); return paramiko.SFTP_OK
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
    def rename(self,old,new):
        try:
            oldname=pathlib.PurePosixPath(old).name; newname=pathlib.PurePosixPath(new).name
            names={'commit-'+mode+'.bin' for mode in self.commit_faults}
            if oldname in names and newname.startswith(oldname+'.portharbor-') and newname.endswith('.bak'):
                self.commit_name=oldname; self.commit_mark('backup-requested')
                if oldname=='commit-drop-before-backup.bin': return self.commit_drop()
                self.path(old).rename(self.path(new)); self.commit_mark('backed-up')
                if oldname=='commit-drop-after-backup.bin': return self.commit_drop()
                return paramiko.SFTP_OK
            if self.commit_name==newname and oldname.startswith(newname+'.portharbor-'):
                if oldname.endswith('.part'):
                    self.commit_mark('publish-requested')
                    if newname in {'commit-stall-restore.bin','commit-reject-publish.bin','commit-reject-restore.bin','commit-drop-after-restore.bin'}:
                        self.commit_mark('publish-rejected'); return paramiko.SFTP_PERMISSION_DENIED
                    if newname=='commit-drop-before-publish.bin': return self.commit_drop()
                    self.path(old).rename(self.path(new)); self.commit_mark('published')
                    if newname=='commit-drop-after-publish.bin': return self.commit_drop()
                    return paramiko.SFTP_OK
                if oldname.endswith('.bak'):
                    self.commit_mark('restore-requested')
                    if newname=='commit-stall-restore.bin': return self.commit_stall('restore-stalled')
                    if newname=='commit-reject-restore.bin':
                        self.commit_mark('restore-rejected'); return paramiko.SFTP_PERMISSION_DENIED
                    self.path(old).rename(self.path(new)); self.commit_mark('restored')
                    if newname=='commit-drop-after-restore.bin': return self.commit_drop()
                    return paramiko.SFTP_OK
            self.path(old).rename(self.path(new)); return paramiko.SFTP_OK
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
    def posix_rename(self,old,new):
        try: self.path(old).replace(self.path(new)); return paramiko.SFTP_OK
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
key=paramiko.RSAKey.generate(2048)
ssh=socket.socket(); ssh.bind(('127.0.0.1',0)); ssh.listen(10)
def accept_ssh():
    while True:
        client,_=ssh.accept()
        def serve(client=client):
            try:
                transport=paramiko.Transport(client); transport.add_server_key(key); transport.set_subsystem_handler('sftp',paramiko.SFTPServer,Sftp)
                auth=Auth(); auth.transport=transport; transport.start_server(server=auth)
                while transport.is_active(): time.sleep(.1)
            except Exception as e: print(type(e).__name__,str(e),flush=True)
        threading.Thread(target=serve,daemon=True).start()
threading.Thread(target=accept_ssh,daemon=True).start()
class FaultDtp(DTPHandler):
    drop_scheduled=False
    def drop(self,direction):
        if not self.drop_scheduled:
            self.drop_scheduled=True
            (ftp_root/'faults'/(direction+'.dropped')).write_text('65536')
            # Defer to the I/O loop: FTPHandler can still be installing this data
            # channel while send() first runs, so closing it inline corrupts setup.
            self.cmd_channel.ioloop.call_later(0,self.cmd_channel.close)
    def handle_read(self):
        super().handle_read()
        if self.file_obj and pathlib.Path(self.file_obj.name).name.startswith('drop-upload.bin.portharbor-') and self.tot_bytes_received>=65536:
            self.drop('upload')
    handle_read_event=handle_read
    def send(self,data):
        dropping=self.file_obj and pathlib.Path(self.file_obj.name).name=='drop-download.bin'
        if dropping: data=data[:max(0,65536-self.tot_bytes_sent)]
        count=super().send(data)
        if dropping and self.tot_bytes_sent>=65536: self.drop('download')
        return count
    def close(self):
        if not self._closed and self.file_obj and pathlib.Path(self.file_obj.name).name=='reject-download.bin' and self.transfer_finished:
            self._resp=('426 Test fixture rejected final download confirmation.',self.cmd_channel.log)
            (ftp_root/'faults'/'confirmation.rejected').write_text(str(self.tot_bytes_sent))
        super().close()
class FaultFtp(FTPHandler):
    dtp_handler=FaultDtp
    use_sendfile=False
auth=DummyAuthorizer(); auth.add_user('easytest','easytest-only',str(ftp_root),perm='elradfmwMT')
FaultFtp.authorizer=auth
server=FTPServer(('127.0.0.1',0),FaultFtp)
(root/'servers.json').write_text(json.dumps({'ftpPort':server.socket.getsockname()[1],'sftpPort':ssh.getsockname()[1]}))
print('READY '+str(root/'servers.json'),flush=True)
server.serve_forever()
