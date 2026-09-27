using System.Globalization;
using System.Text.Json;

namespace Toml.Tests;

/// <summary>
/// Compara a saída deste analisador com o JSON que a suíte traz.
/// </summary>
/// <remarks>
/// <para>
/// A comparação não é de texto. Dois JSON podem descrever a mesma coisa e ter
/// bytes diferentes — a ordem das chaves de um objeto não significa nada, e
/// <c>1e3</c> e <c>1000.0</c> são o mesmo número fracionário. Comparar as
/// strings faria o teste reprovar acertos.
/// </para>
/// <para>
/// Então a comparação é estrutural, e o tipo de cada folha decide como
/// comparar o valor: <c>float</c> como número (com <c>nan</c> igual a
/// <c>nan</c>, que é a única igualdade que o IEEE 754 nega), <c>integer</c>
/// como inteiro de 64 bits, e o resto como texto exato.
/// </para>
/// </remarks>
public static class Conferencia
{
    public static bool Iguais(string meu, string esperado, out string diferenca)
    {
        using var umDocumento = JsonDocument.Parse(meu);
        using var outroDocumento = JsonDocument.Parse(esperado);

        return Iguais(umDocumento.RootElement, outroDocumento.RootElement, "$", out diferenca);
    }

    private static bool Iguais(
        JsonElement meu, JsonElement esperado, string caminho, out string diferenca)
    {
        diferenca = string.Empty;

        if (meu.ValueKind != esperado.ValueKind)
        {
            diferenca = $"{caminho}: obtive {meu.ValueKind} e esperava {esperado.ValueKind}";

            return false;
        }

        switch (esperado.ValueKind)
        {
            case JsonValueKind.Object:
                return ObjetosIguais(meu, esperado, caminho, out diferenca);

            case JsonValueKind.Array:
                return VetoresIguais(meu, esperado, caminho, out diferenca);

            default:
                if (meu.ToString() == esperado.ToString())
                {
                    return true;
                }

                diferenca = $"{caminho}: obtive {meu} e esperava {esperado}";

                return false;
        }
    }

    private static bool ObjetosIguais(
        JsonElement meu, JsonElement esperado, string caminho, out string diferenca)
    {
        diferenca = string.Empty;

        // Um objeto com `type` e `value` é uma folha, e não uma tabela de duas
        // chaves. A distinção existe porque o protocolo da suíte usa objeto
        // para as duas coisas.
        if (EhFolha(esperado, out var tipoEsperado, out var valorEsperado))
        {
            if (!EhFolha(meu, out var meuTipo, out var meuValor))
            {
                diferenca = $"{caminho}: esperava um valor {tipoEsperado} e obtive uma tabela";

                return false;
            }

            if (meuTipo != tipoEsperado)
            {
                diferenca = $"{caminho}: obtive o tipo {meuTipo} e esperava {tipoEsperado}";

                return false;
            }

            if (ValoresIguais(tipoEsperado, meuValor, valorEsperado))
            {
                return true;
            }

            diferenca = $"{caminho} ({tipoEsperado}): obtive \"{meuValor}\" "
                + $"e esperava \"{valorEsperado}\"";

            return false;
        }

        var minhas = meu.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var dele = esperado.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        var sobrando = minhas.Except(dele).OrderBy(c => c, StringComparer.Ordinal).ToList();
        var faltando = dele.Except(minhas).OrderBy(c => c, StringComparer.Ordinal).ToList();

        if (sobrando.Count > 0 || faltando.Count > 0)
        {
            diferenca = $"{caminho}: chaves a mais [{string.Join(", ", sobrando)}], "
                + $"a menos [{string.Join(", ", faltando)}]";

            return false;
        }

        foreach (var chave in dele)
        {
            if (!Iguais(meu.GetProperty(chave), esperado.GetProperty(chave),
                    $"{caminho}.{chave}", out diferenca))
            {
                return false;
            }
        }

        return true;
    }

    private static bool VetoresIguais(
        JsonElement meu, JsonElement esperado, string caminho, out string diferenca)
    {
        diferenca = string.Empty;

        var meus = meu.EnumerateArray().ToList();
        var deles = esperado.EnumerateArray().ToList();

        if (meus.Count != deles.Count)
        {
            diferenca = $"{caminho}: obtive {meus.Count} itens e esperava {deles.Count}";

            return false;
        }

        for (var i = 0; i < deles.Count; i++)
        {
            if (!Iguais(meus[i], deles[i], $"{caminho}[{i}]", out diferenca))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EhFolha(JsonElement elemento, out string tipo, out string valor)
    {
        tipo = string.Empty;
        valor = string.Empty;

        if (elemento.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!elemento.TryGetProperty("type", out var tipoElemento)
            || !elemento.TryGetProperty("value", out var valorElemento))
        {
            return false;
        }

        if (tipoElemento.ValueKind != JsonValueKind.String
            || valorElemento.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        // Uma tabela TOML pode ter exatamente as chaves `type` e `value`, e aí
        // ela **não** é uma folha. O desempate é o número de chaves: uma folha
        // tem duas e nada mais.
        var quantas = elemento.EnumerateObject().Count();

        if (quantas != 2)
        {
            return false;
        }

        tipo = tipoElemento.GetString() ?? string.Empty;
        valor = valorElemento.GetString() ?? string.Empty;

        return true;
    }

    private static bool ValoresIguais(string tipo, string meu, string esperado) => tipo switch
    {
        "float" => FracionariosIguais(meu, esperado),
        "integer" => long.Parse(meu, CultureInfo.InvariantCulture)
                     == long.Parse(esperado, CultureInfo.InvariantCulture),
        _ => string.Equals(meu, esperado, StringComparison.Ordinal),
    };

    private static bool FracionariosIguais(string meu, string esperado)
    {
        var um = Fracionario(meu);
        var outro = Fracionario(esperado);

        // `nan == nan` é falso no IEEE 754, e aqui os dois têm de bater: a
        // suíte espera um `nan` e o analisador produziu um `nan`, e isso é um
        // acerto.
        if (double.IsNaN(um) && double.IsNaN(outro))
        {
            return true;
        }

        return um.Equals(outro);
    }

    private static double Fracionario(string texto) => texto switch
    {
        "inf" or "+inf" => double.PositiveInfinity,
        "-inf" => double.NegativeInfinity,
        "nan" or "+nan" or "-nan" => double.NaN,
        _ => double.Parse(texto, NumberStyles.Float, CultureInfo.InvariantCulture),
    };
}
