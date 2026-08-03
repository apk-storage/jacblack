namespace JacBlack.Infrastructure.Security
{
    public readonly struct JacBlackAccessResult
    {
        public static JacBlackAccessResult Allow { get; } = new(true, 0, false);

        public bool IsAllowed { get; }
        public int DenyStatusCode { get; }
        public bool SetPrivateNetworkHeaderOnDeny { get; }

        JacBlackAccessResult(bool isAllowed, int denyStatusCode, bool setPrivateNetworkHeaderOnDeny)
        {
            IsAllowed = isAllowed;
            DenyStatusCode = denyStatusCode;
            SetPrivateNetworkHeaderOnDeny = setPrivateNetworkHeaderOnDeny;
        }

        public static JacBlackAccessResult Deny(int statusCode, bool setPrivateNetworkHeader = true)
            => new(false, statusCode, setPrivateNetworkHeader);
    }
}
