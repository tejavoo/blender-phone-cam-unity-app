namespace CamLinkPro.Networking
{
    /// Hands a successfully-scanned QR pairing from ScanQrScreen to Landing
    /// without connecting instantly — spec 4.2 wants one look at what it
    /// parsed before committing.
    public static class PendingScan
    {
        public static PairingInfo? Result;
    }
}
