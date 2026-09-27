using System.Globalization;
using System.Text;
using Toml.Core;

namespace Toml.Cli;

/// <summary>A linha de comando.</summary>
public static class Programa
{
    private const string Ajuda = """
        toml — o analisador de TOML 1.0.0 do zero

          ver <arquivo>       a arvore, indentada, com o tipo de cada valor
          json <arquivo>      a codificacao que a suite de conformidade usa
          conferir <arquivo>  so diz se e valido, e onde esta o erro
          suite [pasta]       roda a suite oficial inteira
          tipos               os oito tipos do formato, com exemplo

          toml ver pyproject.toml
          toml conferir config.toml
          toml tipos
        """;

    public static int Main(string[] argumentos)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (argumentos.Length == 0 || argumentos[0] is "ajuda" or "-h" or "--help")
        {
            Console.WriteLine(Ajuda);

            return 0;
        }

        try
        {
            return argumentos[0] switch
            {
                "ver" => Ver(Exigir(argumentos, 1, "arquivo")),
                "json" => Json(Exigir(argumentos, 1, "arquivo")),
                "conferir" => Conferir(Exigir(argumentos, 1, "arquivo")),
                "tipos" => Tipos(),
                "suite" => Suite(argumentos.Length > 1 ? argumentos[1] : PastaPadraoDaSuite()),
                _ => Desconhecido(argumentos[0]),
            };
        }
        catch (FileNotFoundException erro)
        {
            Console.Error.WriteLine($"arquivo nao encontrado: {erro.FileName}");

            return 1;
        }
        catch (DirectoryNotFoundException)
        {
            Console.Error.WriteLine("pasta nao encontrada");

            return 1;
        }
    }

    private static string Exigir(string[] argumentos, int posicao, string nome)
    {
        if (posicao >= argumentos.Length)
        {
            Console.Error.WriteLine($"falta o {nome}");
            Console.Error.WriteLine(Ajuda);
            Environment.Exit(2);
        }

        return argumentos[posicao];
    }

    private static int Desconhecido(string comando)
    {
        Console.Error.WriteLine($"comando desconhecido: {comando}");
        Console.Error.WriteLine(Ajuda);

        return 2;
    }

    private static int Ver(string caminho)
    {
        var tabela = Analisador.Analisar(File.ReadAllBytes(caminho));

        Desenhar(tabela, string.Empty);

        return 0;
    }

    /// <summary>Desenha a árvore com o tipo de cada valor à direita.</summary>
    private static void Desenhar(Valor valor, string recuo)
    {
        switch (valor)
        {
            case Valor.Tabela tabela:
                // As chaves saem alinhadas: uma arvore em que o `=` dança de
                // coluna em coluna e mais dificil de ler do que uma em que ele
                // fica parado.
                var largura = tabela.Pares()
                    .Where(p => p.Value is not Valor.Tabela)
                    .Select(p => p.Key.Length)
                    .DefaultIfEmpty(0)
                    .Max();

                foreach (var (chave, dentro) in tabela.Pares())
                {
                    switch (dentro)
                    {
                        case Valor.Tabela:
                        case Valor.Lista { Itens.Count: > 0 } lista
                            when lista.Itens[0] is Valor.Tabela:
                            Console.WriteLine($"{recuo}{chave}");
                            Desenhar(dentro, recuo + "  ");
                            break;

                        default:
                            Console.WriteLine(
                                $"{recuo}{chave.PadRight(largura)} = {Resumo(dentro)}"
                                + $"   {Cinza(dentro.NomeNaSuite())}");
                            break;
                    }
                }

                return;

            case Valor.Lista lista:
                for (var i = 0; i < lista.Quantos; i++)
                {
                    Console.WriteLine($"{recuo}[{i}]");
                    Desenhar(lista.Itens[i], recuo + "  ");
                }

                return;
        }
    }

    /// <summary>Cinza claro, para o tipo nao competir com o valor.</summary>
    private static string Cinza(string texto) =>
        $"\u001b[90m{texto}\u001b[0m";

    private static string Resumo(Valor valor) => valor switch
    {
        Valor.Texto texto when texto.Conteudo.Length > 48 =>
            $"\"{texto.Conteudo[..45]}...\"",
        Valor.Texto texto => $"\"{texto.Conteudo}\"",
        Valor.Lista lista => $"[{string.Join(", ", lista.Itens.Select(Resumo))}]",
        _ => ParaJson.ComoTexto(valor),
    };

    private static int Json(string caminho)
    {
        Console.WriteLine(ParaJson.Indentado(Analisador.Analisar(File.ReadAllBytes(caminho))));

        return 0;
    }

    private static int Conferir(string caminho)
    {
        var bytes = File.ReadAllBytes(caminho);

        try
        {
            var tabela = Analisador.Analisar(bytes);

            Console.WriteLine($"  valido — {tabela.Quantas} chave(s) na raiz");

            return 0;
        }
        catch (ErroDeToml erro)
        {
            Console.WriteLine($"  invalido — linha {erro.Linha}, coluna {erro.Coluna}");
            Console.WriteLine($"  {erro.Explicacao}");
            Console.WriteLine();

            MostrarLinha(bytes, erro);

            return 1;
        }
    }

    /// <summary>A linha do erro, com um cursor embaixo da coluna.</summary>
    private static void MostrarLinha(byte[] bytes, ErroDeToml erro)
    {
        string texto;

        try
        {
            texto = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // O arquivo nem é UTF-8: não há linha para mostrar.
            return;
        }

        var linhas = texto.Replace("\r\n", "\n").Split('\n');

        if (erro.Linha < 1 || erro.Linha > linhas.Length)
        {
            return;
        }

        var linha = linhas[erro.Linha - 1];
        var numero = erro.Linha.ToString(CultureInfo.InvariantCulture);

        Console.WriteLine($"  {numero} | {linha}");
        Console.WriteLine($"  {new string(' ', numero.Length)} | "
            + new string(' ', Math.Max(0, erro.Coluna - 1)) + "^");
    }

    private static int Tipos()
    {
        const string exemplo = """
            texto     = "uma frase"
            inteiro   = 42
            grande    = 1_000_000
            hexa      = 0xdeadbeef
            fracao    = 3.14
            infinito  = inf
            logico    = true
            instante  = 1979-05-27T07:32:00Z
            local     = 1979-05-27T07:32:00
            dia       = 1979-05-27
            hora      = 07:32:00
            lista     = [1, 2, 3]
            emLinha   = { a = 1, b = 2 }
            """;

        Console.WriteLine();
        Console.WriteLine("  Os tipos do TOML 1.0.0");
        Console.WriteLine("  ----------------------");
        Console.WriteLine();

        foreach (var linha in exemplo.Split('\n'))
        {
            Console.WriteLine($"    {linha.TrimEnd()}");
        }

        Console.WriteLine();
        Console.WriteLine("  e o que cada um vira:");
        Console.WriteLine();

        Desenhar(Analisador.Analisar(Encoding.UTF8.GetBytes(exemplo)), "    ");

        Console.WriteLine();
        Console.WriteLine("  Os quatro de data e hora sao o que nenhum outro formato de");
        Console.WriteLine("  configuracao tem, e sao o motivo declarado de o TOML existir.");
        Console.WriteLine();

        return 0;
    }

    private static string PastaPadraoDaSuite() =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "tests", "Toml.Tests", "suite");

    private static int Suite(string pasta)
    {
        if (!Directory.Exists(pasta))
        {
            Console.Error.WriteLine($"a pasta da suite nao existe: {Path.GetFullPath(pasta)}");

            return 1;
        }

        var validos = 0;
        var invalidos = 0;
        var problemas = new List<string>();

        foreach (var caminho in Directory
                     .EnumerateFiles(Path.Combine(pasta, "valid"), "*.toml",
                         SearchOption.AllDirectories)
                     .OrderBy(c => c, StringComparer.Ordinal))
        {
            try
            {
                Analisador.Analisar(File.ReadAllBytes(caminho));
                validos++;
            }
            catch (ErroDeToml erro)
            {
                problemas.Add($"{Path.GetFileName(caminho)}: {erro.Message}");
            }
        }

        foreach (var caminho in Directory
                     .EnumerateFiles(Path.Combine(pasta, "invalid"), "*.toml",
                         SearchOption.AllDirectories)
                     .OrderBy(c => c, StringComparer.Ordinal))
        {
            try
            {
                Analisador.Analisar(File.ReadAllBytes(caminho));
                problemas.Add($"{Path.GetFileName(caminho)}: foi aceito e devia ser recusado");
            }
            catch (ErroDeToml)
            {
                invalidos++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"  {validos} validos lidos");
        Console.WriteLine($"  {invalidos} invalidos recusados");
        Console.WriteLine($"  {validos + invalidos} de {validos + invalidos + problemas.Count}");

        foreach (var problema in problemas.Take(20))
        {
            Console.WriteLine($"    {problema}");
        }

        Console.WriteLine();

        return problemas.Count == 0 ? 0 : 1;
    }
}
