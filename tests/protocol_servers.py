"""Loopback-only integration fixtures; requires paramiko and pyftpdlib, never shipped in app."""
import argparse, json, os, pathlib, socket, threading, time
import paramiko
from pyftpdlib.authorizers import DummyAuthorizer
from pyftpdlib.handlers import FTPHandler
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
class Sftp(paramiko.SFTPServerInterface):
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
            f=os.fdopen(fd,mode); handle=paramiko.SFTPHandle(flags); handle.readfile=handle.writefile=f; return handle
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
    def mkdir(self,path,attr):
        try: self.path(path).mkdir(); return paramiko.SFTP_OK
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
    def rmdir(self,path):
        try: self.path(path).rmdir(); return paramiko.SFTP_OK
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
    def remove(self,path):
        try: self.path(path).unlink(); return paramiko.SFTP_OK
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
    def rename(self,old,new):
        try: self.path(old).rename(self.path(new)); return paramiko.SFTP_OK
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
                transport=paramiko.Transport(client); transport.add_server_key(key); transport.set_subsystem_handler('sftp',paramiko.SFTPServer,Sftp); transport.start_server(server=Auth())
                while transport.is_active(): time.sleep(.1)
            except Exception as e: print(type(e).__name__,str(e),flush=True)
        threading.Thread(target=serve,daemon=True).start()
threading.Thread(target=accept_ssh,daemon=True).start()
auth=DummyAuthorizer(); auth.add_user('easytest','easytest-only',str(ftp_root),perm='elradfmwMT')
FTPHandler.authorizer=auth
server=FTPServer(('127.0.0.1',0),FTPHandler)
(root/'servers.json').write_text(json.dumps({'ftpPort':server.socket.getsockname()[1],'sftpPort':ssh.getsockname()[1]}))
print('READY '+str(root/'servers.json'),flush=True)
server.serve_forever()
