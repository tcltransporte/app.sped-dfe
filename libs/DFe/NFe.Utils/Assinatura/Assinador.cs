/********************************************************************************/
/* Projeto: Biblioteca ZeusNFe                                                  */
/* Biblioteca C# para emissão de Nota Fiscal Eletrônica - NFe e Nota Fiscal de  */
/* Consumidor Eletrônica - NFC-e (http://www.nfe.fazenda.gov.br)                */
/*                                                                              */
/* Direitos Autorais Reservados (c) 2014 Adenilton Batista da Silva             */
/*                                       Zeusdev Tecnologia LTDA ME             */
/*                                                                              */
/*  Você pode obter a última versão desse arquivo no GitHub                     */
/* localizado em https://github.com/adeniltonbs/Zeus.Net.NFe.NFCe               */
/*                                                                              */
/*                                                                              */
/*  Esta biblioteca é software livre; você pode redistribuí-la e/ou modificá-la */
/* sob os termos da Licença Pública Geral Menor do GNU conforme publicada pela  */
/* Free Software Foundation; tanto a versão 2.1 da Licença, ou (a seu critério) */
/* qualquer versão posterior.                                                   */
/*                                                                              */
/*  Esta biblioteca é distribuída na expectativa de que seja útil, porém, SEM   */
/* NENHUMA GARANTIA; nem mesmo a garantia implícita de COMERCIABILIDADE OU      */
/* ADEQUAÇÃO A UMA FINALIDADE ESPECÍFICA. Consulte a Licença Pública Geral Menor*/
/* do GNU para mais detalhes. (Arquivo LICENÇA.TXT ou LICENSE.TXT)              */
/*                                                                              */
/*  Você deve ter recebido uma cópia da Licença Pública Geral Menor do GNU junto*/
/* com esta biblioteca; se não, escreva para a Free Software Foundation, Inc.,  */
/* no endereço 59 Temple Street, Suite 330, Boston, MA 02111-1307 USA.          */
/* Você também pode obter uma copia da licença em:                              */
/* http://www.opensource.org/licenses/lgpl-license.php                          */
/*                                                                              */
/* Zeusdev Tecnologia LTDA ME - adenilton@zeusautomacao.com.br                  */
/* http://www.zeusautomacao.com.br/                                             */
/* Rua Comendador Francisco josé da Cunha, 111 - Itabaiana - SE - 49500-000     */
/********************************************************************************/
using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using DFe.Utils;
using DFe.Utils.Assinatura;
using Shared.DFe.Utils;
using Signature = DFe.Classes.Assinatura.Signature;

namespace NFe.Utils.Assinatura
{
    public static class Assinador
    {
        /// <summary>
        ///     Obtém a assinatura de um objeto serializável
        /// </summary>
        /// <typeparam name="T">Tipo do objeto a ser assinado</typeparam>
        /// <param name="objeto">Objeto a ser assinado</param>
        /// <param name="id">Id para URI do objeto <see cref="Signature"/></param>
        /// <param name="configuracaoServico">Configuração do serviço</param>
        /// <returns>Retorna um objeto do tipo Classes.Assinatura.Signature, contendo a assinatura do objeto passado como parâmetro</returns>
        public static Signature ObterAssinatura<T>(T objeto, string id, ConfiguracaoServico configuracaoServico = null) where T : class
        {
            var cfgServico = configuracaoServico ?? ConfiguracaoServico.Instancia;

            X509Certificate2 certificadoDigital = null;
            try
            {
                certificadoDigital = CertificadoDigital.ObterCertificado(cfgServico.Certificado);
                return ObterAssinatura<T>(objeto, id, certificadoDigital, cfgServico.Certificado.ManterDadosEmCache, cfgServico.Certificado.SignatureMethodSignedXml, cfgServico.Certificado.DigestMethodReference, cfgServico.RemoverAcentos);
            }
            finally
            {
                if (!cfgServico.Certificado.ManterDadosEmCache)
                    certificadoDigital?.Reset();
            }
        }

        /// <summary>
        ///     Obtém a assinatura de um objeto serializável
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="objeto"></param>
        /// <param name="id"></param>
        /// <param name="certificadoDigital">Informe o certificado digital</param>
        /// <param name="manterDadosEmCache">Validador para manter o certificado em cache</param>
        /// <param name="signatureMethod"></param>
        /// <param name="digestMethod"></param>
        /// <param name="cfgServicoRemoverAcentos"></param>
        /// <returns>Retorna um objeto do tipo Classes.Assinatura.Signature, contendo a assinatura do objeto passado como parâmetro</returns>
        public static Signature ObterAssinatura<T>(T objeto, string id, X509Certificate2 certificadoDigital,
            bool manterDadosEmCache = false, string signatureMethod = "http://www.w3.org/2000/09/xmldsig#rsa-sha1",
            string digestMethod = "http://www.w3.org/2000/09/xmldsig#sha1", bool cfgServicoRemoverAcentos = false) where T : class
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new Exception("Id para assinatura não informado.");

            try
            {
                var xml = cfgServicoRemoverAcentos
                    ? FuncoesXml.ClasseParaXmlString(objeto).RemoverAcentos()
                    : FuncoesXml.ClasseParaXmlString(objeto);

                if (!xml.Contains($"Id=\"{id}\""))
                    throw new Exception($"Id '{id}' não encontrado no XML.");

                var documento = new XmlDocument
                {
                    PreserveWhitespace = true
                };
                documento.LoadXml(xml);

                var rsa = certificadoDigital.GetRSAPrivateKey();
                if (rsa == null)
                    throw new Exception("Chave privada RSA não encontrada no certificado.");

                var signedXml = new SignedXml(documento)
                {
                    SigningKey = rsa
                };

                signedXml.SignedInfo.SignatureMethod = signatureMethod;

                var reference = new Reference
                {
                    Uri = "#" + id,
                    DigestMethod = digestMethod
                };

                reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
                reference.AddTransform(new XmlDsigC14NTransform());

                signedXml.AddReference(reference);

                var keyInfo = new KeyInfo();
                keyInfo.AddClause(new KeyInfoX509Data(certificadoDigital));
                signedXml.KeyInfo = keyInfo;

                signedXml.ComputeSignature();

                var xmlDigitalSignature = signedXml.GetXml();
                return FuncoesXml.XmlStringParaClasse<Signature>(xmlDigitalSignature.OuterXml);
            }
            catch (Exception ex)
            {
                throw new Exception(
                    "Erro ao obter assinatura XML com certificado A1. " +
                    ex.InnerException?.Message ?? ex.Message,
                    ex
                );
            }

        }
    }
}
