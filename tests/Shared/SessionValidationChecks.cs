using PortHarbor;

namespace PortHarbor.Checks;

internal static class SessionValidationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var invalid = new Session { Name = "", Host = "bad host", Port = 0 };
        var errors = invalid.GetValidationErrors();
        check(errors.Select(e => e.Field).SequenceEqual(["Name", "Host", "Port"]),
            "session validation reports all invalid network fields in focus order");
        string? message = null;
        try { invalid.Validate(); } catch (ArgumentException error) { message = error.Message; }
        check(message == errors[0].Message && invalid.Name == "" && invalid.Host == "bad host",
            "throwing session validation uses the same first error without mutating its input");
        check(new Session { Protocol = (Protocol)999, Host = "" }.GetValidationErrors().Select(e => e.Field).SequenceEqual(["Protocol"]),
            "unknown protocols produce a protocol field error without interpreting protocol-specific fields");
        check(new Session { Protocol = Protocol.Serial, SerialPort = "", BaudRate = 0, DataBits = 4 }.GetValidationErrors()
            .Select(e => e.Field).SequenceEqual(["SerialPort", "BaudRate", "DataBits"]),
            "serial validation identifies each invalid field without requiring a network host");
        check(new Session { Protocol = Protocol.Shell, Executable = "" }.GetValidationErrors().Single().Field == "Executable"
            && new Session { Protocol = Protocol.Shell, Host = "bad host", Port = 0 }.GetValidationErrors().Count == 0,
            "shell validation locates the program error and ignores inactive network fields");
        check(new Session { Protocol = Protocol.RDP, Host = "desktop.invalid:3390" }.GetValidationErrors().Single().Field == "Host"
            && new Session { Protocol = Protocol.RDP, Host = "2001:db8::1", Port = 3390 }.GetValidationErrors().Count == 0,
            "RDP host-port mistakes map to the host field while an IPv6 literal remains valid");
        check(Enum.GetValues<Protocol>().All(p => new Session { Protocol = p, Host = "fixture.invalid", Port = Session.DefaultPort(p) }.GetValidationErrors().Count == 0),
            "all seven valid protocol configurations remain accepted by field-aware validation");
        invalid.Name = "Fixed"; invalid.Host = "fixture.invalid"; invalid.Port = 22;
        check(invalid.GetValidationErrors().Count == 0 && errors.Count == 3,
            "corrected session fields clear new validation results without modifying a previous error snapshot");
    }
}
