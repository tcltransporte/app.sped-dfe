using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace application.services
{
    public class Certificate
    {
        public static X509Certificate2 Loader(string base64, string password)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(base64))
                    throw new Exception("Certificado não informado.");

                if (string.IsNullOrWhiteSpace(password))
                    throw new Exception("Senha do certificado não informada.");

                var certBytes = Convert.FromBase64String(base64);

                var cert = X509CertificateLoader.LoadPkcs12(certBytes, password);

                if (cert == null)
                    throw new Exception("Certificado inválido.");

                // Verifica se possui chave privada
                if (!cert.HasPrivateKey)
                    throw new Exception("Certificado inválido: não possui chave privada.");

                // Verifica vencimento
                var now = DateTime.Now;

                if (now < cert.NotBefore)
                    throw new Exception($"Certificado ainda não está válido. Validade inicia em {cert.NotBefore:dd/MM/yyyy HH:mm:ss}.");

                if (now > cert.NotAfter)
                    throw new Exception($"Certificado vencido em {cert.NotAfter:dd/MM/yyyy HH:mm:ss}.");

                // Verifica validade da cadeia / confiança
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // pode usar Online se quiser
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

                bool isValid = chain.Build(cert);

                if (!isValid)
                {
                    var errors = string.Join(" | ",
                        chain.ChainStatus
                             .Select(s => $"{s.Status} - {s.StatusInformation?.Trim()}"));

                    throw new Exception($"Certificado inválido. Detalhes: {errors}");
                }

                return cert;
            }
            catch (CryptographicException ex)
            {
                // Normalmente cai aqui quando a senha está errada ou o PKCS12 é inválido
                throw new Exception("Senha inválida ou certificado corrompido/inválido.", ex);
            }
            catch (FormatException ex)
            {
                throw new Exception("O conteúdo do certificado não está em Base64 válido.", ex);
            }
        }
    }
}
