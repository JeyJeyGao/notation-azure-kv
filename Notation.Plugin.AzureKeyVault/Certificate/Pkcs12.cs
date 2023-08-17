using System.Security.Cryptography.Pkcs;
using Notation.Plugin.Protocol;

namespace Notation.Plugin.AzureKeyVault.Certificate
{
    static class Pkcs12
    {
        /// <summary>
        /// Re-encode the PKCS12 data to remove the MAC and keys.
        /// The macOS doesn't support PKCS12 with non-encrypted MAC.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        /// <exception cref="ValidationException"></exception>
        public static byte[] ReEncode(byte[] data)
        {
            Pkcs12Info pfx = Pkcs12Info.Decode(data, out _);
            // only remove the MAC if it is password protected
            if (pfx.IntegrityMode != Pkcs12IntegrityMode.Password)
            {
                return data;
            }
            // verify the MAC with null password
            if (!pfx.VerifyMac(ReadOnlySpan<char>.Empty))
            {
                throw new ValidationException("Invalid MAC or the MAC password is not null");
            }

            // re-build PFX without MAC
            Pkcs12Builder pfxBuilder = new Pkcs12Builder();
            foreach (var safeContent in pfx.AuthenticatedSafe)
            {
                // decrypt with null password
                safeContent.Decrypt(ReadOnlySpan<byte>.Empty);

                // create a newSafeContent and only contains the certificate bag
                var newSafeContent = new Pkcs12SafeContents();
                foreach (var bag in safeContent.GetBags())
                {
                    if (bag is Pkcs12CertBag)
                    {
                        newSafeContent.AddSafeBag(bag);
                    }
                }
                pfxBuilder.AddSafeContentsUnencrypted(newSafeContent);
            }
            pfxBuilder.SealWithoutIntegrity();
            return pfxBuilder.Encode();
        }
    }
}
