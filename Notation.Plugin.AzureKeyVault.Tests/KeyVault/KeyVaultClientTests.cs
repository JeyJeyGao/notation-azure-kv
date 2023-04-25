using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Moq;
using Xunit;
using Notation.Plugin.Protocol;

namespace Notation.Plugin.AzureKeyVault.Client.Tests
{
    public class KeyVaultClientTests
    {
        [Fact]
        public void TestConstructorWithKeyId()
        {
            string keyId = "https://myvault.vault.azure.net/keys/my-key/123";

            KeyVaultClient keyVaultClient = new KeyVaultClient(keyId);

            Assert.Equal("my-key", keyVaultClient.Name);
            Assert.Equal("123", keyVaultClient.Version);
            Assert.Equal(keyId, keyVaultClient.KeyId);
        }

        [Fact]
        public void TestConstructorWithKeyVaultUrlNameVersion()
        {
            string keyVaultUrl = "https://myvault.vault.azure.net";
            string name = "my-key";
            string version = "123";

            KeyVaultClient keyVaultClient = new KeyVaultClient(keyVaultUrl, name, version);

            Assert.Equal(name, keyVaultClient.Name);
            Assert.Equal(version, keyVaultClient.Version);
            Assert.Equal($"{keyVaultUrl}/keys/{name}/{version}", keyVaultClient.KeyId);
        }

        [Theory]
        [InlineData("https://myvault.vault.azure.net/invalid/my-key/123")]
        [InlineData("https://myvault.vault.azure.net/keys/my-key")]
        [InlineData("https://myvault.vault.azure.net/keys/my-key/")]
        [InlineData("http://myvault.vault.azure.net/keys/my-key/123")]
        public void TestConstructorWithInvalidKeyId(string invalidKeyId)
        {
            Assert.Throws<ValidationException>(() => new KeyVaultClient(invalidKeyId));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void TestConstructorWithEmptyKeyId(string invalidKeyId)
        {
            Assert.Throws<ArgumentNullException>(() => new KeyVaultClient(invalidKeyId));
        }

        private class TestableKeyVaultClient : KeyVaultClient
        {
            public TestableKeyVaultClient(string keyVaultUrl, string name, string version, CryptographyClient cryptoClient)
                : base(keyVaultUrl, name, version)
            {
                this._cryptoClient = new Lazy<CryptographyClient>(() => cryptoClient);
            }

            public TestableKeyVaultClient(string keyVaultUrl, string name, string version, CertificateClient certificateClient)
                : base(keyVaultUrl, name, version)
            {
                this._certificateClient = new Lazy<CertificateClient>(() => certificateClient);
            }
        }

        private TestableKeyVaultClient CreateMockedKeyVaultClient(SignResult signResult)
        {
            var mockCryptoClient = new Mock<CryptographyClient>(new Uri("https://fake.vault.azure.net/keys/fake-key/123"), new Mock<TokenCredential>().Object);
            mockCryptoClient.Setup(c => c.SignDataAsync(It.IsAny<SignatureAlgorithm>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(signResult);

            return new TestableKeyVaultClient("https://fake.vault.azure.net", "fake-key", "123", mockCryptoClient.Object);
        }

        private TestableKeyVaultClient CreateMockedKeyVaultClient(KeyVaultCertificate certificate)
        {
            var mockCertificateClient = new Mock<CertificateClient>(new Uri("https://fake.vault.azure.net/certificates/fake-certificate/123"), new Mock<TokenCredential>().Object);
            mockCertificateClient.Setup(c => c.GetCertificateVersionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(certificate, new Mock<Response>().Object));

            return new TestableKeyVaultClient("https://fake.vault.azure.net", "fake-certificate", "123", mockCertificateClient.Object);
        }

        [Fact]
        public async Task TestSignAsyncReturnsExpectedSignature()
        {
            var signResult = CryptographyModelFactory.SignResult(
                keyId: "https://fake.vault.azure.net/keys/fake-key/123",
                signature: new byte[] { 1, 2, 3 },
                algorithm: SignatureAlgorithm.RS256);

            TestableKeyVaultClient keyVaultClient = CreateMockedKeyVaultClient(signResult);
            byte[] payload = new byte[] { 4, 5, 6 };

            byte[] signature = await keyVaultClient.SignAsync(SignatureAlgorithm.RS256, payload);

            Assert.Equal(signResult.Signature, signature);
        }

        [Fact]
        public async Task TestSignAsyncThrowsExceptionOnInvalidKeyId()
        {
            var signResult = CryptographyModelFactory.SignResult(
                keyId: "https://fake.vault.azure.net/keys/invalid-key/123",
                signature: new byte[] { 1, 2, 3 },
                algorithm: SignatureAlgorithm.RS256);

            TestableKeyVaultClient keyVaultClient = CreateMockedKeyVaultClient(signResult);
            byte[] payload = new byte[] { 4, 5, 6 };

            await Assert.ThrowsAsync<PluginException>(async () => await keyVaultClient.SignAsync(SignatureAlgorithm.RS256, payload));
        }

        [Fact]
        public async Task TestSignAsyncThrowsExceptionOnInvalidAlgorithm()
        {
            var signResult = CryptographyModelFactory.SignResult(
                keyId: "https://fake.vault.azure.net/keys/fake-key/123",
                signature: new byte[] { 1, 2, 3 },
                algorithm: SignatureAlgorithm.RS384);

            TestableKeyVaultClient keyVaultClient = CreateMockedKeyVaultClient(signResult);
            byte[] payload = new byte[] { 4, 5, 6 };
            await Assert.ThrowsAsync<PluginException>(async () => await keyVaultClient.SignAsync(SignatureAlgorithm.RS256, payload));
        }

        [Fact]
        public async Task GetCertificateAsync_ReturnsCertificate()
        {
            var testCertificate = new X509Certificate2(Path.Combine(Directory.GetCurrentDirectory(), "TestData", "rsa_2048_cert.pem"));
            var signResult = CryptographyModelFactory.SignResult(
                keyId: "https://fake.vault.azure.net/keys/fake-key/123",
                signature: new byte[] { 1, 2, 3 },
                algorithm: SignatureAlgorithm.RS384);

            var keyVaultCertificate = CertificateModelFactory.KeyVaultCertificate(
                properties: CertificateModelFactory.CertificateProperties(version: "123"),
                cer: testCertificate.RawData);

            var keyVaultClient = CreateMockedKeyVaultClient(keyVaultCertificate);
            var certificate = await keyVaultClient.GetCertificateAsync();

            Assert.NotNull(certificate);
            Assert.IsType<X509Certificate2>(certificate);
            Assert.Equal("123", keyVaultCertificate.Properties.Version);
            Assert.Equal(testCertificate.RawData, certificate.RawData);
        }

        [Fact]
        public async Task GetCertificateAsyncThrowValidationException()
        {
            var testCertificate = new X509Certificate2(Path.Combine(Directory.GetCurrentDirectory(), "TestData", "rsa_2048_cert.pem"));
            var signResult = CryptographyModelFactory.SignResult(
                keyId: "https://fake.vault.azure.net/keys/fake-key/123",
                signature: new byte[] { 1, 2, 3 },
                algorithm: SignatureAlgorithm.RS384);

            var keyVaultCertificate = CertificateModelFactory.KeyVaultCertificate(
                properties: CertificateModelFactory.CertificateProperties(version: "1234"),
                cer: testCertificate.RawData);

            var keyVaultClient = CreateMockedKeyVaultClient(keyVaultCertificate);

            await Assert.ThrowsAsync<ValidationException>(async () => await keyVaultClient.GetCertificateAsync());
        }
    }
}
