# Third-party notices

This file and the `licenses/` directory contain notices for third-party dependencies only. They do not grant a license to PortHarbor's own code. The dependencies retain their own copyrights and licenses; applicable license texts are included in `licenses/` and must accompany redistribution of those dependencies.

| Package | Version | License | Upstream |
| --- | --- | --- | --- |
| SSH.NET | 2026.0.0 | MIT | https://github.com/sshnet/SSH.NET |
| FluentFTP | 55.1.0 | MIT | https://github.com/robinrodricks/FluentFTP |
| BouncyCastle.Cryptography | 2.7.0 | MIT, with notices in upstream license | https://github.com/bcgit/bc-csharp |
| Microsoft.Extensions.Logging.Abstractions | 8.0.3 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | MIT | https://github.com/dotnet/runtime |
| System.IO.Ports | 8.0.0 | MIT | https://github.com/dotnet/runtime |

Framework-dependent releases require Microsoft .NET 8 Desktop Runtime, installed separately under its own license. Windows ConPTY, DPAPI, and the external Remote Desktop client are operating system components and are not redistributed here.

Self-contained releases also include Microsoft.NETCore.App.Runtime.win-x64 and Microsoft.WindowsDesktop.App.Runtime.win-x64 8.0.32, under the MIT license. Their license texts and runtime third-party notices are included in `licenses/`. Application and runtime notices are embedded in the executable and available under Help → Third-party licenses, including in single-file portable releases.

Installation wizards are generated with Inno Setup 6.7.4 by Jordan Russell and Martijn Laan (https://jrsoftware.org/). Its license is in `licenses/innosetup-6.7.4-LICENSE.txt`. The unmodified Simplified Chinese messages come from the official translations page (https://jrsoftware.org/files/istrans/), maintained by Zhenghan Yang; source and SHA-256 are recorded in `docs/DISTRIBUTION.md`. The compiler is a build tool, not an application runtime dependency.

License texts were extracted from the exact NuGet packages except SSH.NET, whose MIT license was retrieved from the upstream 2026.0.0 tag. Microsoft package third-party notices are also included. The complete dependency graph and integrity hashes are in `src/PortHarbor/packages.lock.json`.

Python paramiko, pyftpdlib and their dependencies are used solely for optional loopback tests. They are not included in the application or source archives as installed libraries. See each project's license when installing those tools separately.
