using System.Xml;
using System.Xml.Serialization;

namespace application.services
{
    public class Extensions
    {

        public static T GetEnumByXmlEnumAttribute<T>(string value) where T : Enum
        {
            foreach (var field in typeof(T).GetFields())
            {
                var attribute = Attribute.GetCustomAttribute(field, typeof(XmlEnumAttribute)) as XmlEnumAttribute;
                if (attribute != null && attribute.Name == value)
                {
                    return (T)field.GetValue(null);
                }
            }
            throw new ArgumentException($"Valor '{value}' não encontrado no Enum {typeof(T).Name}");
        }

        /// <summary>
        /// Remove caracteres de edição (quebras de linha, tabs, indentação) entre as tags e
        /// nas extremidades dos valores. A SEFAZ rejeita XML formatado (ex.: rejeição 588).
        /// Deve ser chamado ANTES de assinar o documento.
        /// </summary>
        public static void RemoverCaracteresDeEdicao(XmlNode? node)
        {
            if (node == null) return;

            var hasElementChildren = node.ChildNodes.Cast<XmlNode>().Any(c => c.NodeType == XmlNodeType.Element);

            foreach (var child in node.ChildNodes.Cast<XmlNode>().ToList())
            {
                switch (child.NodeType)
                {
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        node.RemoveChild(child);
                        break;

                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                        if (hasElementChildren)
                        {
                            if (string.IsNullOrWhiteSpace(child.Value))
                                node.RemoveChild(child);
                            else
                                child.Value = child.Value!.Trim();
                        }
                        else
                        {
                            var value = child.Value ?? string.Empty;
                            value = value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
                            // Remove BOM, caractere de substituição (texto mal decodificado) e controles
                            value = new string(value.Where(ch => ch != '\uFEFF' && ch != '\uFFFD' && !char.IsControl(ch)).ToArray());
                            child.Value = value.Trim();
                        }
                        break;

                    case XmlNodeType.Element:
                        RemoverCaracteresDeEdicao(child);
                        break;
                }
            }
        }

        public static void LimparCaracteresEspeciais(XmlNode? node)
        {

            if (node != null)
            {

                if (node?.NodeType == XmlNodeType.Text)
                {
                    node.Value = XmlSanitizer.RemoverAcentos(node?.Value);
                }

                foreach (XmlNode child in node?.ChildNodes)
                {
                    LimparCaracteresEspeciais(child);
                }

            }

        }

    }
}