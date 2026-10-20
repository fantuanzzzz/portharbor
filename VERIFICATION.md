# Validation record — 0.1.0

Date: 2026-10-20, Windows x64. .NET SDK 8.0.426, .NET Desktop Runtime 8.0.22.

- Release build: 0 warnings, 0 errors.
- Complete protocol run: 42 checks passed (`artifacts/integration-results.json`). FTP and SSH/SFTP were actual loopback servers implemented with pyftpdlib 2.2.0 and paramiko 5.1.0, not mocked client methods. Telnet used an actual loopback TCP peer. Shell used Windows ConPTY with cmd.exe.
- Final build script: 33 default checks passed (`test-results.json`); includes the strengthened Shell check requiring command execution output rather than just echoing the typed command.
- UI: main window rendered; all seven protocol editors opened and closed. Local Shell in the terminal UI produced command output.
- The previous PortHarbor publish smoke test initialized the real main window and exited with code 0 after WM_CLOSE (`artifacts/release-smoke.json`). PortHarbor release packages are produced by the updated `build.ps1`.

Covered: DPAPI encryption/reload, export without credentials, import IDs, atomic config backup, corrupted config preservation, host key confirmation/rejection, ANSI parsing/fuzz bounds/CJK, Telnet fragmentation and ordered output, Shell input/output/resize/exit, cancelled local download preserving the original, FTP/SFTP Unicode filenames and byte round trips/overwrite/rename/delete, SSH PTY login/input/disconnect, RDP file generation without a password.

Not validated against real devices/servers: physical serial ports, actual RDP sign-in, explicit FTPS, private-key authentication, arbitrary production SSH/FTP servers, IME and complex full-screen terminal applications. These paths are implemented but require broader compatibility testing.

The app uses a minimal ANSI/VT terminal and external Windows RDP client. This validation record does not claim full MobaXterm feature or terminal compatibility.
