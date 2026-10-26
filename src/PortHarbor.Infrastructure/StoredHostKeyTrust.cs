namespace PortHarbor;

internal sealed class StoredHostKeyTrust(ConfigurationState state) : IHostKeyTrust
{
    public bool CheckHostKey(string host, int port, string fingerprint, Func<string, bool> confirm)
    {
        var name = $"[{host.ToLowerInvariant()}]:{port}";
        lock (state.Gate)
        {
            if (state.Data.HostKeys.TryGetValue(name, out var known))
            {
                if (known != fingerprint) throw new HostKeyRejectedException($"SSH 主机密钥发生变化，已拒绝连接。\n主机：{name}\n已保存：{known}\n本次：{fingerprint}\n请向服务器管理员核实后，在设置文件中移除该主机的旧记录。");
                return true;
            }
        }
        // A modal UI confirmation must never hold the persistence lock.
        if (!confirm($"首次连接 {name}\n主机密钥指纹：\n{fingerprint}\n\n请与服务器管理员核对。信任并保存此密钥？")) return false;
        lock (state.Gate)
        {
            if (state.Data.HostKeys.TryGetValue(name, out var concurrent))
            {
                if (concurrent != fingerprint) throw new InvalidOperationException("另一连接已保存不同的 SSH 主机密钥，已拒绝连接。");
                return true;
            }
            state.Data.HostKeys[name] = fingerprint;
            try { state.Save(); }
            catch { state.Data.HostKeys.Remove(name); throw; }
            return true;
        }
    }
}
