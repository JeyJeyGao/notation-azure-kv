using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Notation.Plugin.AzureKeyVault.Certificate
{
    class CustomX509Store
    {
        // Read the certificates from PEM file and add the certificates to the
        // X509 store.
        public static X509Store Create(string pemFilePath)
        {
            // Create a X509 store and remove all certs.
            var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            // TODO - check is it correct
            store.RemoveRange(store.Certificates);
            Console.WriteLine("Yes    {0,4}  {1}, {2}", store.Certificates.Count, store.Name, store.Location);

            // Load the certificates from PEM file.
            string pemContent = File.ReadAllText(pemFilePath);
            string[] pemCertificates = pemContent.Split(
                new[] { "-----END CERTIFICATE-----" }, StringSplitOptions.RemoveEmptyEntries);

            // Add the certificates to the store.
            foreach (string pemCertificate in pemCertificates)
            {
                string certContent = $"{pemCertificate}-----END CERTIFICATE-----";
                byte[] certBytes = ConvertPemToDer(certContent);
                X509Certificate2 cert = new X509Certificate2(certBytes);
                store.Add(cert);
            }
            return store;
        }

        /// <summary>
        /// Convert PEM to DER.
        /// </summary>
        private static byte[] ConvertPemToDer(string pem)
        {
            StringBuilder builder = new StringBuilder();
            string[] lines = pem.Split('\n');

            foreach (string line in lines)
            {
                if (!line.StartsWith("-----"))
                {
                    builder.Append(line);
                }
            }

            return Convert.FromBase64String(builder.ToString());
        }
    }
}