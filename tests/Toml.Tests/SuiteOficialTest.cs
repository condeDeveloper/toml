using System.Text;
using Toml.Core;
using Xunit.Abstractions;

namespace Toml.Tests;

/// <summary>
/// A suíte oficial do <c>toml-test</c> — o juiz deste projeto.
/// </summary>
/// <remarks>
/// <para>
/// Ela é mantida pelo próprio grupo do TOML e é o que toda implementação usa
/// para dizer que é conforme. São <b>709 arquivos</b> para o TOML 1.0.0, e a
/// proporção entre eles diz o que é difícil no formato:
/// </para>
/// <list type="bullet">
///   <item><b>208 válidos</b>, cada um com o JSON do resultado esperado.</item>
///   <item><b>501 inválidos</b>, que têm de ser <b>recusados</b>.</item>
/// </list>
/// <para>
/// Dois terços da suíte são arquivos errados. Não é desequilíbrio: é onde está
/// a dificuldade. Ler <c>chave = valor</c> é fácil — o difícil é saber quando
/// uma tabela pode ser estendida, e cada uma dessas 501 respostas é um jeito
/// diferente de errar.
/// </para>
/// </remarks>
public sealed class SuiteOficialTest
{
    private readonly ITestOutputHelper _saida;

    public SuiteOficialTest(ITestOutputHelper saida) => _saida = saida;

    private static string Pasta =>
        Path.Combine(AppContext.BaseDirectory, "suite");

    public static IEnumerable<object[]> Validos()
    {
        foreach (var caminho in Arquivos("valid"))
        {
            yield return [Relativo(caminho)];
        }
    }

    public static IEnumerable<object[]> Invalidos()
    {
        foreach (var caminho in Arquivos("invalid"))
        {
            yield return [Relativo(caminho)];
        }
    }

    private static IEnumerable<string> Arquivos(string qual) =>
        Directory.EnumerateFiles(Path.Combine(Pasta, qual), "*.toml", SearchOption.AllDirectories)
                 .OrderBy(c => c, StringComparer.Ordinal);

    private static string Relativo(string caminho) =>
        Path.GetRelativePath(Pasta, caminho).Replace('\\', '/');

    /// <summary>
    /// O arquivo é lido como UTF-8 sem deixar o .NET adivinhar a codificação.
    /// </summary>
    /// <remarks>
    /// A especificação diz que um documento TOML <b>é</b> UTF-8, e ponto. Ler
    /// com a página de código da máquina faria a suíte passar no Linux e
    /// falhar no Windows, que é o pior resultado possível num teste.
    /// </remarks>
    private static byte[] Bytes(string relativo) =>
        File.ReadAllBytes(Path.Combine(Pasta, relativo));

    /// <summary>O JSON esperado, esse sim ja decodificado.</summary>
    private static string Ler(string relativo) =>
        Encoding.UTF8.GetString(Bytes(relativo));

    [Theory]
    [MemberData(nameof(Validos))]
    public void OsValidosSaoLidosEDaoOResultadoEsperado(string relativo)
    {
        var tabela = Analisador.Analisar(Bytes(relativo));
        var meu = ParaJson.Compacto(tabela);
        var esperado = Ler(Path.ChangeExtension(relativo, ".json"));

        Assert.True(
            Conferencia.Iguais(meu, esperado, out var diferenca),
            $"{relativo}\n  {diferenca}\n\n  obtive:   {meu}\n  esperava: {esperado.Trim()}");
    }

    [Theory]
    [MemberData(nameof(Invalidos))]
    public void OsInvalidosSaoRecusados(string relativo)
    {
        var bytes = Bytes(relativo);
        var erro = Record.Exception(() => Analisador.Analisar(bytes));

        Assert.True(erro is not null, $"{relativo} foi aceito e devia ser recusado");

        // Recusar com a exceção certa importa: um `NullReferenceException` ou
        // um `IndexOutOfRangeException` também "recusa" o arquivo, e recusa
        // por acidente — o analisador tropeçou em vez de ter entendido.
        Assert.True(
            erro is ErroDeToml,
            $"{relativo} foi recusado com {erro!.GetType().Name} em vez de ErroDeToml: "
            + erro.Message);
    }

    [Fact]
    public void ASuiteEstaInteira()
    {
        // Se alguem aparar a suite, os testes acima passam a provar muito
        // menos. Esta conferencia existe para que isso nao aconteca calado.
        var validos = Arquivos("valid").Count();
        var invalidos = Arquivos("invalid").Count();

        Assert.Equal(208, validos);
        Assert.Equal(501, invalidos);

        // Todo valido tem o JSON do resultado ao lado.
        foreach (var caminho in Arquivos("valid"))
        {
            Assert.True(
                File.Exists(Path.ChangeExtension(caminho, ".json")),
                $"{Relativo(caminho)} nao tem o .json ao lado");
        }

        _saida.WriteLine($"{validos} validos, {invalidos} invalidos, {validos + invalidos} ao todo");
    }

    [Fact]
    public void OsInvalidosCobremAsFamiliasQueImportam()
    {
        // Sem isto, uma suite de 501 arquivos sobre a mesma coisa passaria por
        // uma suite ampla. Estas sao as pastas que o toml-test usa, e cada uma
        // e um jeito diferente de o formato ser mal escrito.
        var familias = Arquivos("invalid")
            .Select(Relativo)
            .Select(c => c.Split('/')[1])
            .ToHashSet(StringComparer.Ordinal);

        foreach (var esperada in new[]
                 {
                     "array", "bool", "control", "datetime", "encoding", "float",
                     "inline-table", "integer", "key", "local-date", "local-datetime",
                     "local-time", "spec-1.0.0", "string", "table",
                 })
        {
            Assert.Contains(esperada, familias);
        }

        _saida.WriteLine($"familias: {string.Join(", ", familias.OrderBy(f => f, StringComparer.Ordinal))}");
    }
}
