using System.Diagnostics.CodeAnalysis;

namespace Toml.Core;

/// <summary>
/// Um valor de TOML. São oito tipos, e a lista fechada é de propósito.
/// </summary>
public abstract record Valor
{
    private Valor()
    {
    }

    public sealed record Texto(string Conteudo) : Valor
    {
        public override string ToString() => $"\"{Conteudo}\"";
    }

    public sealed record Inteiro(long Numero) : Valor
    {
        public override string ToString() => Numero.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public sealed record Fracionario(double Numero) : Valor
    {
        public override string ToString() => Numero.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public sealed record Logico(bool Verdade) : Valor
    {
        public override string ToString() => Verdade ? "true" : "false";
    }

    public sealed record Momento(DataHora Quando) : Valor
    {
        public override string ToString() => Quando.Canonico();
    }

    public sealed record Lista : Valor
    {
        private readonly List<Valor> _itens;

        public Lista() => _itens = [];

        public Lista(IEnumerable<Valor> itens) => _itens = [.. itens];

        public IReadOnlyList<Valor> Itens => _itens;

        public int Quantos => _itens.Count;

        /// <summary>
        /// Se a lista foi escrita com colchetes, e não criada por
        /// <c>[[tabela]]</c>.
        /// </summary>
        /// <remarks>
        /// A diferença importa e a suíte cobra: uma lista escrita à mão é
        /// <b>fechada</b>, e um cabeçalho <c>[[a]]</c> não pode acrescentar
        /// elementos a ela.
        /// </remarks>
        public bool EscritaComColchetes { get; init; }

        internal void Acrescentar(Valor valor) => _itens.Add(valor);

        public override string ToString() => $"[{string.Join(", ", _itens)}]";
    }

    /// <summary>
    /// Uma tabela — e as quatro bandeiras que decidem se ela pode crescer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A parte difícil do TOML não é ler valores: é decidir <b>quando uma
    /// tabela pode ser estendida</b>. É onde mora a maioria dos 501 arquivos
    /// inválidos da suíte, e as regras não se deduzem — elas se leem, uma por
    /// uma, na especificação.
    /// </para>
    /// <para>
    /// Uma tabela pode ter nascido de quatro jeitos, e cada um a deixa aberta
    /// a coisas diferentes:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Por cabeçalho</b> (<c>[a]</c>): aceita chaves; não aceita um
    ///   segundo <c>[a]</c>.</item>
    ///   <item><b>Implicitamente</b> (o <c>a</c> de <c>[a.b]</c>): aceita um
    ///   <c>[a]</c> depois, uma vez só.</item>
    ///   <item><b>Por chave pontilhada</b> (o <c>a</c> de <c>a.b = 1</c>):
    ///   aceita outras chaves pontilhadas no mesmo bloco; <b>não</b> aceita um
    ///   cabeçalho <c>[a]</c> depois.</item>
    ///   <item><b>Em linha</b> (<c>{ }</c>): fechada para sempre, no instante
    ///   em que a chave fecha.</item>
    /// </list>
    /// </remarks>
    public sealed record Tabela : Valor
    {
        private readonly Dictionary<string, Valor> _pares = [];
        private readonly List<string> _ordem = [];

        public IReadOnlyList<string> Chaves => _ordem;

        public int Quantas => _ordem.Count;

        /// <summary>Nasceu de um cabeçalho <c>[a]</c> escrito no arquivo.</summary>
        public bool DefinidaPorCabecalho { get; internal set; }

        /// <summary>Nasceu porque alguém escreveu <c>[a.b]</c> e o <c>a</c> faltava.</summary>
        public bool CriadaImplicitamente { get; internal set; }

        /// <summary>Foi escrita entre chaves. Fechada para sempre.</summary>
        public bool EmLinha { get; internal set; }

        /// <summary>Nasceu do <c>a</c> de uma chave pontilhada <c>a.b = 1</c>.</summary>
        public bool CriadaPorChavePontilhada { get; internal set; }

        /// <summary>
        /// É um elemento de array de tabelas — o <c>[[a]]</c> cria estes.
        /// </summary>
        public bool ElementoDeArrayDeTabelas { get; internal set; }

        public Valor this[string chave] => _pares[chave];

        public bool Tem(string chave) => _pares.ContainsKey(chave);

        public bool TentarObter(string chave, [NotNullWhen(true)] out Valor? valor) =>
            _pares.TryGetValue(chave, out valor);

        internal void Por(string chave, Valor valor)
        {
            if (!_pares.ContainsKey(chave))
            {
                // A ordem de inserção é guardada porque a saída em JSON da
                // suíte é comparada como objeto, mas a saída de linha de
                // comando é lida por gente — e uma tabela que sai fora da
                // ordem em que foi escrita é difícil de conferir.
                _ordem.Add(chave);
            }

            _pares[chave] = valor;
        }

        public IEnumerable<KeyValuePair<string, Valor>> Pares()
        {
            foreach (var chave in _ordem)
            {
                yield return new KeyValuePair<string, Valor>(chave, _pares[chave]);
            }
        }

        public override string ToString() =>
            $"{{{string.Join(", ", _ordem.Select(c => $"{c} = {_pares[c]}"))}}}";
    }

    /// <summary>O nome do tipo na codificação que a suíte de conformidade usa.</summary>
    public string NomeNaSuite() => this switch
    {
        Texto => "string",
        Inteiro => "integer",
        Fracionario => "float",
        Logico => "bool",
        Momento momento => momento.Quando.NomeNaSuite(),
        Lista => "array",
        Tabela => "table",
        _ => throw new InvalidOperationException($"tipo desconhecido: {GetType().Name}"),
    };
}
