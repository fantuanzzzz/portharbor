using Renci.SshNet;
using Renci.SshNet.Common;

namespace PortHarbor.Checks;

internal static class SshAuthenticationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (string missing in new[] { "", "  " })
        {
            bool rejected = false;
            try { SshClientSupport.SshInfo("auth.invalid", 22, missing, "missing-private-key", "", out _); }
            catch (ArgumentException error) { rejected = error.ParamName == "username"; }
            check(rejected, "SSH requires a username before reading private keys or starting network authentication");
        }
        var prompts = new[] { new AuthenticationPrompt(1, false, "一次性验证码"), new AuthenticationPrompt(2, true, "设备名称") };
        var args = new AuthenticationPromptEventArgs("zh-CN", "需要完成第二步验证", "", prompts);
        SshAuthenticationChallenge? received = null;
        SshClientSupport.Respond(args, challenge => { received = challenge; return ["otp-value", "trusted-device"]; });
        check(received?.Instruction == "需要完成第二步验证"
            && received.Prompts.SequenceEqual([new SshAuthenticationPrompt("一次性验证码", false), new SshAuthenticationPrompt("设备名称", true)])
            && prompts[0].Response == "otp-value" && prompts[1].Response == "trusted-device",
            "keyboard-interactive forwards each challenge prompt and applies responses in order");

        bool cancelled = false;
        try { SshClientSupport.Respond(new AuthenticationPromptEventArgs("", "", "", [new AuthenticationPrompt(3, false, "取消测试")]), _ => null); }
        catch (SshAuthenticationCancelledException e) { cancelled = !e.Message.Contains("otp-value", StringComparison.Ordinal); }
        check(cancelled, "interactive authentication cancellation stops the round without exposing a response");

        var info = SshClientSupport.SshInfo("auth.invalid", 22, "user", "", "password", out var key,
            _ => ["response"]);
        check(key == null && info.AuthenticationMethods.Count == 2
            && info.AuthenticationMethods[0] is PasswordAuthenticationMethod
            && info.AuthenticationMethods[1] is KeyboardInteractiveAuthenticationMethod,
            "SSH password connections retain password auth and add keyboard-interactive fallback");
    }
}
