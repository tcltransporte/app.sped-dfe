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