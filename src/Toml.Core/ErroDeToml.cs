namespace Toml.Core;

/// <summary>
/// Um arquivo TOML que não é TOML válido.
/// </summary>
/// <remarks>
/// <para>
/// O erro carrega <b>linha e coluna</b>, e isso não é enfeite. Dois terços da
/// suíte oficial de conformidade são arquivos que têm de ser <b>recusados</b> —
/// 501 de 709 —, e um analisador que recusa todos dizendo "arquivo inválido"
/// passa num teste de exceção sem ter entendido nada.
/// </para>
/// <para>
/// Dizer <i>onde</i> e <i>por quê</i> é também o que separa um formato de
/// configuração que se usa de um que se abandona: quem escreve um
/// <c>pyproject.toml</c> errado às onze da noite precisa da linha, não de um
/// "erro de sintaxe".
/// </para>
/// </remarks>
public sealed class ErroDeToml : Exception
{
    public ErroDeToml(string mensagem, int linha, int coluna)
        : base($"linha {linha}, coluna {coluna}: {mensagem}")
    {
        Explicacao = mensagem;
        Linha = linha;
        Coluna = coluna;
    }

    /// <summary>A mensagem sem a posição na frente.</summary>
    public string Explicacao { get; }

    /// <summary>A linha, contada a partir de 1.</summary>
    public int Linha { get; }

    /// <summary>A coluna, contada a partir de 1.</summary>
    public int Coluna { get; }
}
