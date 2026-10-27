using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using iTextSharp.text.pdf.security;

namespace PDFQFZ.Library
{
    /// <summary>
    /// Signs through the certificate's RSA provider without exporting private
    /// key parameters. This supports CNG-backed and non-exportable keys.
    /// </summary>
    internal sealed class X509Certificate2RsaSignature : IExternalSignature
    {
        private readonly X509Certificate2 certificate;

        public X509Certificate2RsaSignature(X509Certificate2 certificate)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }
            if (!certificate.HasPrivateKey)
            {
                throw new ArgumentException(
                    "The certificate does not contain a private key.",
                    nameof(certificate));
            }

            this.certificate = certificate;
        }

        public byte[] Sign(byte[] message)
        {
            using (RSA rsa = certificate.GetRSAPrivateKey())
            {
                if (rsa == null)
                {
                    throw new CryptographicException(
                        "The certificate's private key is not an RSA key.");
                }

                return rsa.SignData(
                    message,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
            }
        }

        public string GetHashAlgorithm()
        {
            return DigestAlgorithms.SHA256;
        }

        public string GetEncryptionAlgorithm()
        {
            return "RSA";
        }
    }
}
