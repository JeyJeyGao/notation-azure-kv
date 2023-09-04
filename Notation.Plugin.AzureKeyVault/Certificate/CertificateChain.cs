using System.Security.Cryptography.X509Certificates;
using Notation.Plugin.Protocol;

namespace Notation.Plugin.AzureKeyVault.Certificate
{
    /// <summary>
    /// Helper class to build certificate chain.
    /// </summary>
    static class CertificateChain
    {
        /// <summary>
        /// Build the certificate chain from the certificate bundle by matching
        /// the issuer and subject distinguished name.
        /// 
        /// It doesn't verify the certificate path.
        /// </summary>
        /// <param name="certs"></param>
        /// <returns>
        /// The certificate chain. The first certificate is the leaf certificate.
        /// </returns>
        /// <exception cref="PluginException"></exception>
        public static X509Certificate2Collection Build(X509Certificate2Collection certs)
        {
            if (certs.Count == 0)
            {
                throw new PluginException("The certificate bundle is empty");
            }

            if (certs.Count == 1){
                if (certs[0].SubjectName.Name != certs[0].IssuerName.Name)
                {
                    throw new PluginException("The certificate bundle only contains one certificate but it is not a self-signed certificate. Please complete the certificate bundle by `ca_certs` through plugin config.");
                }
                return certs;
            }

            // subject distinguished name -> certificate map
            var certMap = new Dictionary<string, X509Certificate2>();
            var issuerSet = new HashSet<string>();
            foreach (var cert in certs)
            {
                if (certMap.ContainsKey(cert.SubjectName.Name))
                {
                    throw new PluginException($"The certificate bundle contains duplicated certificates: {cert.SubjectName.Name}");
                }
                certMap[cert.SubjectName.Name] = cert;
                issuerSet.Add(cert.IssuerName.Name);
            }

            // count the leaf certificate
            if (certs.Count(x => !issuerSet.Contains(x.SubjectName.Name)) != 1)
            {
                // AKV certificates always contain the leaf certificate
                throw new PluginException("The certificate bundle may contains multiple leaf certificates");
            }
            var leafCert = certs.First(x => !issuerSet.Contains(x.SubjectName.Name));

            // build the certificate chain
            X509Certificate2Collection chain = new X509Certificate2Collection();
            var currentCert = leafCert;
            while (currentCert != null)
            {
                chain.Add(currentCert);

                // root certificate
                if (currentCert.SubjectName.Name == currentCert.IssuerName.Name)
                {
                    break;
                }

                // check if the issuer is found in the certificate bundle
                if (!certMap.ContainsKey(currentCert.IssuerName.Name))
                {
                    throw new PluginException($"The certificate bundle is not complete. The issuer of {currentCert.SubjectName.Name} is not found.");
                }
                currentCert = certMap[currentCert.IssuerName.Name];
            }

            if (chain.Count != certs.Count)
            {
                throw new PluginException($"The certificate bundle has {certs.Count()} certificates but the certificate chain only has {chain.Count()} certficates.");
            }
            return chain;
        }
    }
}
