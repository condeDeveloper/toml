using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Toml.Core;

/// <summary>
/// A codificação em JSON que a suíte de conformidade usa para comparar.
/// </summary>
/// <remarks>
/// <para>
/// Ela não é o JSON que uma pessoa escreveria. Todo valor vira um objeto com
/// <c>type</c> e <c>value</c>, e o <c>value</c> é <b>sempre uma string</b>:
/// </para>
/// <code>
///   porta = 8080          →   {"porta": {"type": "integer", "value": "8080"}}
/// </code>
/// <para>
/// Parece redundante e é a única forma de o teste funcionar. O JSON tem um
/// tipo numérico só, e o TOML tem inteiro e fracionário: sem a etiqueta,
/// <c>1</c> e <c>1.0</c> ficariam indistinguíveis, e um analisador que lesse
/// todo número como <c>double</c> passaria na suíte. O <c>value</c> é string
/// pelo mesmo motivo — um inteiro de 64 bits não cabe no número do JSON sem
/// perder dígito.
/// </para>
/// <para>
/// Tabelas e listas não ganham etiqueta: viram objeto e vetor direto. A
/// assimetria é do protocolo, não minha.
/// </para>
/// </remarks>
public static class ParaJson
{
    /// <summary>A forma que a suíte compara, sem espaço sobrando.</summary>
    public static string Compacto(Valor valor) => Escrever(valor, indentado: false);

    /// <summary>A mesma coisa, indentada, para uma pessoa ler.</summary>
    public static string Indentado(Valor valor) => Escrever(valor, indentado: true);

    private static string Escrever(Valor valor, bool indentado)
    {
        var memoria = new MemoryStream();

        using (var escritor = new Utf8JsonWriter(memoria, new JsonWriterOptions
        {
            Indented = indentado,
            // Sem escapar o que não precisa: um acento vira o acento, e não
            // `é`. A suíte compara depois de interpretar o JSON, então
            // tanto faz para ela — e faz muita diferença para quem lê.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            EscreverValor(escritor, valor);
        }

        return Encoding.UTF8.GetString(memoria.ToArray());
    }

    private static void EscreverValor(Utf8JsonWriter escritor, Valor valor)
    {
        switch (valor)
        {
            case Valor.Tabela tabela:
                escritor.WriteStartObject();

                foreach (var (chave, dentro) in tabela.Pares())
                {
                    escritor.WritePropertyName(chave);
                    EscreverValor(escritor, dentro);
                }

                escritor.WriteEndObject();

                return;

            case Valor.Lista lista:
                escritor.WriteStartArray();

                foreach (var item in lista.Itens)
                {
                    EscreverValor(escritor, item);
                }

                escritor.WriteEndArray();

                return;

            default:
                escritor.WriteStartObject();
                escritor.WriteString("type", valor.NomeNaSuite());
                escritor.WriteString("value", ComoTexto(valor));
                escritor.WriteEndObject();

                return;
        }
    }

    /// <summary>O <c>value</c> de um valor simples, sempre como texto.</summary>
    public static string ComoTexto(Valor valor) => valor switch
    {
        Valor.Texto texto => texto.Conteudo,
        Valor.Inteiro inteiro => inteiro.Numero.ToString(CultureInfo.InvariantCulture),
        Valor.Logico logico => logico.Verdade ? "true" : "false",
        Valor.Momento momento => momento.Quando.Canonico(),
        Valor.Fracionario fracionario => Fracionario(fracionario.Numero),
        _ => throw new InvalidOperationException($"{valor.NomeNaSuite()} nao e um valor simples"),
    };

    /// <summary>
    /// Um número fracionário como texto — e o formato "R" é obrigatório aqui.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O <c>ToString()</c> padrão do .NET arredonda para quinze dígitos
    /// significativos, e com isso <c>double</c>s diferentes viram o mesmo
    /// texto. O <c>"R"</c> — de <i>round-trip</i> — dá o menor texto que volta
    /// a ser exatamente o mesmo número.
    /// </para>
    /// <para>
    /// Os três valores especiais saem com o nome que o TOML usa, e não com o
    /// do .NET: <c>inf</c>, <c>-inf</c> e <c>nan</c>, e não
    /// <c>Infinity</c> nem <c>NaN</c>.
    /// </para>
    /// </remarks>
    private static string Fracionario(double numero)
    {
        if (double.IsPositiveInfinity(numero))
        {
            return "inf";
        }

        if (double.IsNegativeInfinity(numero))
        {
            return "-inf";
        }

        if (double.IsNaN(numero))
        {
            return "nan";
        }

        return numero.ToString("R", CultureInfo.InvariantCulture);
    }
}
