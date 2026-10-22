using System.IO;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace QuestPatcher.Zip
{
    internal static class SigningUtility
    {
        /// <summary>
        /// Loads the certificate and private key from PEM data
        /// </summary>
        /// <param name="pemData">The certificate and private key in PEM format</param>
        /// <returns>The loaded certificate and private key</returns>
        /// <exception cref="System.Security.SecurityException">If the certificate or private key failed to load</exception>
        internal static (X509Certificate certificate, AsymmetricKeyParameter privateKey) LoadCertificate(string pemData)
        {
            X509Certificate? cert = null;
            AsymmetricKeyParameter? privateKey = null;
            using (var reader = new StringReader(pemData))
            {
                // Iterate through the PEM objects until we find the public or private key
                var pemReader = new PemReader(reader);
                object pemObject;
                while ((pemObject = pemReader.ReadObject()) != null)
                {
                    cert ??= pemObject as X509Certificate;
                    privateKey ??= (pemObject as AsymmetricCipherKeyPair)?.Private;
                }
            }
            if (cert == null)
                throw new System.Security.SecurityException("Certificate could not be loaded from PEM data.");

            if (privateKey == null)
                throw new System.Security.SecurityException("Private Key could not be loaded from PEM data.");

            return (cert, privateKey);
        }

        /// <summary>
        /// Signs the given data using RSA with a SHA-256 digest, storing the result in PKCS1 format.
        /// </summary>
        /// <param name="data">The data to sign</param>
        /// <param name="privateKey">The private key to sign with</param>
        /// <returns>The signature in PKCS1 format</returns>
        internal static byte[] SignPKCS1(byte[] data, AsymmetricKeyParameter privateKey)
        {
            var signerType = SignerUtilities.GetSigner("SHA256WithRSA");
            signerType.Init(true, privateKey);
            signerType.BlockUpdate(data, 0, data.Length);

            return signerType.GenerateSignature();
        }
    }
}
