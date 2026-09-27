using System.Text;
using Toml.Core;

namespace Toml.Tests;

/// <summary>
/// As peças por dentro.
/// </summary>
/// <remarks>
/// A suíte oficial já diz se a saída está certa, em 711 arquivos. Estes testes
/// existem para outra coisa: mostrar, em casos que cabem numa tela, <b>que
/// regra</b> cada recusa está aplicando — porque um teste que só diz "este
/// arquivo é inválido" não ensina nada a quem for mexer no código depois.
/// </remarks>
public sealed class PecasTest
{
    private static Valor.Tabela Ler(string toml) =>
        Analisador.Analisar(Encoding.UTF8.GetBytes(toml));

    private static ErroDeToml Recusa(string toml)
    {
        var erro = Record.Exception(() => Ler(toml));

        Assert.NotNull(erro);

        return Assert.IsType<ErroDeToml>(erro);
    }

    private static string Json(string toml) => ParaJson.Compacto(Ler(toml));

    // ------------------------------------------------------------------
    // O que o formato promete
    // ------------------------------------------------------------------

    [Fact]
    public void UmArquivoDeConfiguracaoDeVerdade()
    {
        var tabela = Ler("""
            titulo = "CondeClub"

            [servidor]
            porta = 8080
            limite = 2_500

            [[rotas]]
            caminho = "/entrar"
            metodos = ["GET", "POST"]

            [[rotas]]
            caminho = "/sair"
            metodos = ["POST"]
            """);

        // titulo, servidor e rotas -- a raiz tem tres chaves, e cada tabela
        // conta como uma.
        Assert.Equal(3, tabela.Quantas);
        Assert.Equal("CondeClub", Assert.IsType<Valor.Texto>(tabela["titulo"]).Conteudo);

        var servidor = Assert.IsType<Valor.Tabela>(tabela["servidor"]);
        Assert.Equal(8080, Assert.IsType<Valor.Inteiro>(servidor["porta"]).Numero);

        // O sublinhado e so enfeite de leitura: 2_500 e 2500.
        Assert.Equal(2500, Assert.IsType<Valor.Inteiro>(servidor["limite"]).Numero);

        var rotas = Assert.IsType<Valor.Lista>(tabela["rotas"]);
        Assert.Equal(2, rotas.Quantos);
    }

    [Fact]
    public void AOrdemDasChavesSobreviveALeitura()
    {
        // Uma tabela que sai fora da ordem em que foi escrita e dificil de
        // conferir a olho, e o `Dictionary` do .NET nao promete ordem nenhuma.
        var tabela = Ler("z = 1\na = 2\nm = 3\n");

        Assert.Equal(new[] { "z", "a", "m" }, tabela.Chaves);
    }

    // ------------------------------------------------------------------
    // Quando uma tabela pode crescer -- onde estao os 501 invalidos
    // ------------------------------------------------------------------

    [Fact]
    public void UmaTabelaNaoPodeSerDefinidaDuasVezes()
    {
        var erro = Recusa("[a]\nx = 1\n\n[a]\ny = 2\n");

        Assert.Contains("ja foi definida", erro.Explicacao);
        Assert.Equal(4, erro.Linha);
    }

    [Fact]
    public void UmaTabelaCriadaImplicitamentePodeGanharCabecalhoDepois()
    {
        // O `a` de `[a.b]` nasce sem cabecalho proprio, e pode receber um --
        // **uma** vez. E o que permite escrever a tabela de dentro antes da de
        // fora, que e comum quando a de fora so tem uma chave.
        var tabela = Ler("[a.b]\nx = 1\n\n[a]\ny = 2\n");

        var a = Assert.IsType<Valor.Tabela>(tabela["a"]);

        Assert.Equal(2, a.Quantas);
        Assert.Equal(1, Assert.IsType<Valor.Inteiro>(
            Assert.IsType<Valor.Tabela>(a["b"])["x"]).Numero);
    }

    [Fact]
    public void MasSoUmaVez()
    {
        Assert.Contains("ja foi definida", Recusa("[a.b]\n[a]\n[a]\n").Explicacao);
    }

    [Fact]
    public void UmaTabelaCriadaPorChavePontilhadaNaoGanhaCabecalho()
    {
        // `fruta.cor = "vermelha"` cria a tabela `fruta`, e ela nasce fechada
        // para cabecalho. A regra existe para que a ordem de leitura do
        // arquivo seja a ordem de escrita dele: um `[fruta]` depois faria o
        // leitor voltar atras.
        var erro = Recusa("""
            [pomar]
            fruta.cor = "vermelha"

            [pomar.fruta]
            sabor = "doce"
            """);

        Assert.Contains("chave pontilhada", erro.Explicacao);
    }

    [Fact]
    public void MasDuasChavesPontilhadasNaMesmaTabelaConvivem()
    {
        var tabela = Ler("""
            [pomar]
            fruta.cor = "vermelha"
            fruta.sabor = "doce"
            """);

        var fruta = Assert.IsType<Valor.Tabela>(
            Assert.IsType<Valor.Tabela>(tabela["pomar"])["fruta"]);

        Assert.Equal(2, fruta.Quantas);
    }

    [Fact]
    public void UmaTabelaEmLinhaEstaFechadaParaSempre()
    {
        // O momento em que a chave fecha e o momento em que a tabela acaba.
        // Nao ha como acrescentar depois, nem por cabecalho nem por chave
        // pontilhada -- e e isso que faz dela um **valor**, e nao um lugar.
        Assert.Contains("entre chaves", Recusa("a = { b = 1 }\n[a.c]\n").Explicacao);
        Assert.Contains("entre chaves", Recusa("a = { b = 1 }\na.c = 2\n").Explicacao);
    }

    [Fact]
    public void UmaListaEscritaComColchetesNaoRecebeArrayDeTabelas()
    {
        Assert.Contains("colchetes", Recusa("a = []\n[[a]]\n").Explicacao);
    }

    [Fact]
    public void UmArrayDeTabelasCresceACadaCabecalho()
    {
        var tabela = Ler("[[a]]\nx = 1\n[[a]]\nx = 2\n[[a]]\nx = 3\n");
        var lista = Assert.IsType<Valor.Lista>(tabela["a"]);

        Assert.Equal(3, lista.Quantos);
        Assert.Equal(3, Assert.IsType<Valor.Inteiro>(
            Assert.IsType<Valor.Tabela>(lista.Itens[2])["x"]).Numero);
    }

    [Fact]
    public void OCaminhoAtravessaOUltimoElementoDoArray()
    {
        // `[[fruta]]` depois `[fruta.variedade]` pendura a variedade na fruta
        // que acabou de nascer, e nao na primeira. Sem isso, um arquivo com
        // duas frutas poe as duas variedades na mesma.
        var tabela = Ler("""
            [[fruta]]
            nome = "maca"
            [fruta.variedade]
            nome = "gala"

            [[fruta]]
            nome = "banana"
            [fruta.variedade]
            nome = "prata"
            """);

        var frutas = Assert.IsType<Valor.Lista>(tabela["fruta"]);
        var segunda = Assert.IsType<Valor.Tabela>(frutas.Itens[1]);
        var variedade = Assert.IsType<Valor.Tabela>(segunda["variedade"]);

        Assert.Equal("prata", Assert.IsType<Valor.Texto>(variedade["nome"]).Conteudo);
    }

    [Fact]
    public void AMesmaChaveDuasVezesEErro()
    {
        Assert.Contains("ja foi definida", Recusa("a = 1\na = 2\n").Explicacao);
        Assert.Contains("ja foi definida", Recusa("a = { b = 1, b = 2 }\n").Explicacao);
    }

    // ------------------------------------------------------------------
    // Numeros
    // ------------------------------------------------------------------

    [Fact]
    public void ZeroAEsquerdaEErro()
    {
        // Em C, Python 2 e JavaScript antigo, `0755` e um numero em base oito.
        // O TOML recusa a forma em vez de escolher um significado para ela.
        Assert.Contains("zero a esquerda", Recusa("a = 0755\n").Explicacao);
        Assert.Contains("zero a esquerda", Recusa("a = 01\n").Explicacao);

        Assert.Equal(0, Assert.IsType<Valor.Inteiro>(Ler("a = 0\n")["a"]).Numero);
    }

    [Fact]
    public void OSublinhadoTemDeEstarEntreDigitos()
    {
        Assert.Equal(1_000_000, Assert.IsType<Valor.Inteiro>(Ler("a = 1_000_000\n")["a"]).Numero);

        Assert.Contains("sublinhado", Recusa("a = 1__0\n").Explicacao);
        Assert.Contains("sublinhado", Recusa("a = 1_\n").Explicacao);
    }

    [Fact]
    public void OInteiroTemDeCaberEmSessentaEQuatroBits()
    {
        // 2^63 - 1 e o maior. Um a mais nao e "um numero grande": e um arquivo
        // que este analisador nao consegue representar, e dizer isso e melhor
        // do que guardar outro numero em silencio.
        Assert.Equal(long.MaxValue,
            Assert.IsType<Valor.Inteiro>(Ler("a = 9223372036854775807\n")["a"]).Numero);

        Assert.Contains("64 bits", Recusa("a = 9223372036854775808\n").Explicacao);
    }

    [Fact]
    public void AsQuatroBases()
    {
        Assert.Equal(255, Assert.IsType<Valor.Inteiro>(Ler("a = 0xff\n")["a"]).Numero);
        Assert.Equal(493, Assert.IsType<Valor.Inteiro>(Ler("a = 0o755\n")["a"]).Numero);
        Assert.Equal(5, Assert.IsType<Valor.Inteiro>(Ler("a = 0b101\n")["a"]).Numero);
        Assert.Equal(-17, Assert.IsType<Valor.Inteiro>(Ler("a = -17\n")["a"]).Numero);
    }

    [Fact]
    public void InfinitoENaoNumeroSaoValoresDeVerdade()
    {
        Assert.Equal(double.PositiveInfinity,
            Assert.IsType<Valor.Fracionario>(Ler("a = inf\n")["a"]).Numero);
        Assert.Equal(double.NegativeInfinity,
            Assert.IsType<Valor.Fracionario>(Ler("a = -inf\n")["a"]).Numero);
        Assert.True(double.IsNaN(
            Assert.IsType<Valor.Fracionario>(Ler("a = nan\n")["a"]).Numero));
    }

    [Fact]
    public void OPontoDecimalPrecisaDeDigitosDosDoisLados()
    {
        Assert.Contains("digitos depois", Recusa("a = 1.\n").Explicacao);
        Assert.Contains("numero", Recusa("a = .1\n").Explicacao);
    }

    // ------------------------------------------------------------------
    // Datas e horas
    // ------------------------------------------------------------------

    [Fact]
    public void OsQuatroTiposDeMomento()
    {
        var tabela = Ler("""
            instante = 1979-05-27T07:32:00Z
            local    = 1979-05-27T07:32:00
            dia      = 1979-05-27
            hora     = 07:32:00
            """);

        Assert.Equal(DataHora.Especie.Instante,
            Assert.IsType<Valor.Momento>(tabela["instante"]).Quando.Tipo);
        Assert.Equal(DataHora.Especie.DataHoraLocal,
            Assert.IsType<Valor.Momento>(tabela["local"]).Quando.Tipo);
        Assert.Equal(DataHora.Especie.DataLocal,
            Assert.IsType<Valor.Momento>(tabela["dia"]).Quando.Tipo);
        Assert.Equal(DataHora.Especie.HoraLocal,
            Assert.IsType<Valor.Momento>(tabela["hora"]).Quando.Tipo);
    }

    [Fact]
    public void OEspacoNoLugarDoTeMaisDificilDoQueParece()
    {
        // `1979-05-27 07:32:00` e um valor so. Mas `1979-05-27 # comentario`
        // tem o espaco terminando o valor. A diferenca so aparece olhando o
        // que vem depois do espaco.
        Assert.Equal(DataHora.Especie.DataHoraLocal,
            Assert.IsType<Valor.Momento>(Ler("a = 1979-05-27 07:32:00\n")["a"]).Quando.Tipo);

        Assert.Equal(DataHora.Especie.DataLocal,
            Assert.IsType<Valor.Momento>(Ler("a = 1979-05-27 # nasci\n")["a"]).Quando.Tipo);
    }

    [Fact]
    public void ODiaTemDeExistirNoMes()
    {
        Assert.Contains("28 dias", Recusa("a = 1979-02-30\n").Explicacao);
        Assert.Contains("30 dias", Recusa("a = 1979-04-31\n").Explicacao);

        // 2000 foi bissexto e 1900 nao: a excecao da excecao do calendario
        // gregoriano.
        Assert.NotNull(Ler("a = 2000-02-29\n")["a"]);
        Assert.Contains("28 dias", Recusa("a = 1900-02-29\n").Explicacao);
    }

    [Fact]
    public void SessentaSegundosExiste()
    {
        // E o segundo bissexto. Desde 1972 o IERS acrescentou 27 deles, e
        // `2016-12-31T23:59:60Z` e um instante que existiu.
        Assert.NotNull(Ler("a = 2016-12-31T23:59:60Z\n")["a"]);
        Assert.Contains("nao existe", Recusa("a = 2016-12-31T23:59:61Z\n").Explicacao);
    }

    [Fact]
    public void AFracaoCanonizaEmTresDigitos()
    {
        // `.6` e `.600` sao o mesmo instante, e a comparacao precisa de uma
        // forma so.
        Assert.Equal("1987-07-05T17:45:56.600Z",
            Assert.IsType<Valor.Momento>(Ler("a = 1987-07-05T17:45:56.6Z\n")["a"])
                  .Quando.Canonico());

        // E a caixa do `t` e do `z` nao conta -- a gramatica do RFC 3339 nao
        // diferencia.
        Assert.Equal("1987-07-05T17:45:00Z",
            Assert.IsType<Valor.Momento>(Ler("a = 1987-07-05t17:45:00z\n")["a"])
                  .Quando.Canonico());
    }

    // ------------------------------------------------------------------
    // Texto
    // ------------------------------------------------------------------

    [Fact]
    public void OsQuatroJeitosDeEscreverTexto()
    {
        // As quatro aspas em volta sao do C#, e nao do TOML: uma string bruta
        // precisa de um delimitador maior que qualquer sequencia dentro dela.
        var tabela = Ler(""""
            basico   = "linha\tcom\ttabulacao"
            literal  = 'C:\Users\sem\escape'
            multi    = """
            comeca na linha de baixo
            """
            multiLit = '''
            e este tambem
            '''
            """");

        // No basico o `\t` vira tabulacao.
        Assert.Equal("linha\tcom\ttabulacao",
            Assert.IsType<Valor.Texto>(tabela["basico"]).Conteudo);

        // No literal a barra invertida e barra invertida. E por isso que
        // caminho do Windows e expressao regular se escrevem com apostrofe.
        Assert.Equal(@"C:\Users\sem\escape",
            Assert.IsType<Valor.Texto>(tabela["literal"]).Conteudo);

        // A quebra de linha logo depois da abertura nao faz parte do texto --
        // a de antes do fechamento faz.
        Assert.Equal("comeca na linha de baixo\n",
            Assert.IsType<Valor.Texto>(tabela["multi"]).Conteudo);
        Assert.Equal("e este tambem\n",
            Assert.IsType<Valor.Texto>(tabela["multiLit"]).Conteudo);
    }

    [Fact]
    public void ABarraNoFimDaLinhaComeAQuebraEOEspaco()
    {
        // E o que deixa quebrar uma linha longa no fonte sem quebra-la no
        // valor.
        var tabela = Ler(""""
            a = """
            uma frase \
                que continua
            """
            """");

        Assert.Equal("uma frase que continua\n",
            Assert.IsType<Valor.Texto>(tabela["a"]).Conteudo);
    }

    [Fact]
    public void CaractereDeControleNaoEntraNoTexto()
    {
        // Um byte invisivel no meio de um arquivo de configuracao e um jeito
        // de esconder coisa.
        Assert.Contains("controle", Recusa("a = \"x\u0001y\"\n").Explicacao);
        Assert.Contains("controle", Recusa("a = 'x\u0007y'\n").Explicacao);

        // A tabulacao passa: ela e visivel o bastante.
        Assert.Equal("x\ty", Assert.IsType<Valor.Texto>(Ler("a = \"x\ty\"\n")["a"]).Conteudo);
    }

    [Fact]
    public void OEscapeUnicodeRecusaSubstitutoEForaDeFaixa()
    {
        Assert.Equal("A", Assert.IsType<Valor.Texto>(Ler("a = \"\\u0041\"\n")["a"]).Conteudo);

        // Um substituto so existe como metade de um par em UTF-16. Sozinho ele
        // nao e um caractere.
        Assert.Contains("substituto", Recusa("a = \"\\uD800\"\n").Explicacao);

        // E oito digitos hexadecimais nao cabem num int de 32 bits com sinal:
        // ler como `int` faria o analisador estourar em vez de recusar.
        Assert.Contains("acima do maior", Recusa("a = \"\\UFFFFFFFF\"\n").Explicacao);
    }

    [Fact]
    public void EscapeDesconhecidoEErro()
    {
        // O TOML nao tem `\\e` nem `\\x41`. Aceitar em silencio faria dois
        // arquivos com o mesmo texto guardarem coisas diferentes.
        Assert.Contains("escape desconhecido", Recusa("a = \"\\e\"\n").Explicacao);
        Assert.Contains("escape desconhecido", Recusa("a = \"\\x41\"\n").Explicacao);
    }

    // ------------------------------------------------------------------
    // Codificacao
    // ------------------------------------------------------------------

    [Fact]
    public void UmDocumentoTomlEUtf8ESoIsso()
    {
        // 0xFF nao comeca sequencia nenhuma em UTF-8.
        var erro = Record.Exception(() => Analisador.Analisar(new byte[] { 0x61, 0x3D, 0xFF }));

        Assert.IsType<ErroDeToml>(erro);
        Assert.Contains("UTF-8", ((ErroDeToml)erro).Explicacao);
    }

    [Fact]
    public void UmaMarcaDeOrdemDeBytesPassaEDuasNao()
    {
        var comUma = new List<byte> { 0xEF, 0xBB, 0xBF };
        comUma.AddRange("a = 1\n"u8.ToArray());

        Assert.Equal(1, Assert.IsType<Valor.Inteiro>(
            Analisador.Analisar(comUma.ToArray())["a"]).Numero);

        var comDuas = new List<byte> { 0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF };
        comDuas.AddRange("a = 1\n"u8.ToArray());

        Assert.IsType<ErroDeToml>(
            Record.Exception(() => Analisador.Analisar(comDuas.ToArray())));
    }

    [Fact]
    public void RetornoDeCarroSozinhoEErro()
    {
        // Aceitar um faria o arquivo ser lido diferente conforme o sistema que
        // o gravou, que e exatamente o que um formato de configuracao nao pode.
        Assert.Contains("retorno de carro", Recusa("a = 1\rb = 2\n").Explicacao);

        // O par completo e quebra de linha normal.
        Assert.Equal(2, Ler("a = 1\r\nb = 2\r\n").Quantas);
    }

    // ------------------------------------------------------------------
    // O erro diz onde
    // ------------------------------------------------------------------

    [Fact]
    public void OErroTrazLinhaEColuna()
    {
        // Quem escreve um pyproject.toml errado as onze da noite precisa da
        // linha, e nao de um "erro de sintaxe".
        var erro = Recusa("a = 1\nb = 2\nc = ?\n");

        Assert.Equal(3, erro.Linha);
        Assert.Contains("linha 3", erro.Message);
    }

    // ------------------------------------------------------------------
    // A saida em JSON
    // ------------------------------------------------------------------

    [Fact]
    public void AEtiquetaDeTipoSeparaInteiroDeFracionario()
    {
        // Sem ela, `1` e `1.0` ficariam indistinguiveis no JSON -- e um
        // analisador que lesse todo numero como double passaria na suite.
        Assert.Equal("""{"a":{"type":"integer","value":"1"}}""", Json("a = 1\n"));
        Assert.Equal("""{"a":{"type":"float","value":"1"}}""", Json("a = 1.0\n"));
    }

    [Fact]
    public void UmaTabelaComAsChavesTypeEValueNaoEUmaFolha()
    {
        // O protocolo da suite usa objeto para as duas coisas, entao um TOML
        // que tenha exatamente essas duas chaves confunde um comparador
        // ingenuo.
        var meu = Json("""
            [x]
            type = "a"
            value = "b"
            """);

        Assert.True(Conferencia.Iguais(
            meu,
            """
            {"x": {"type": {"type": "string", "value": "a"},
                   "value": {"type": "string", "value": "b"}}}
            """,
            out var diferenca),
            diferenca);
    }
}
