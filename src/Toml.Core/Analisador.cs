using System.Globalization;
using System.Text;

namespace Toml.Core;

/// <summary>
/// O analisador de TOML 1.0.0, por descida recursiva.
/// </summary>
/// <remarks>
/// <para>
/// O TOML nasceu em 2013 de uma frase do Tom Preston-Werner: um formato de
/// configuração "óbvio e mínimo", porque o INI nunca foi especificado — cada
/// programa lia o seu de um jeito — e o YAML é grande demais para caber na
/// cabeça de alguém. A promessa é que o arquivo se leia como um arquivo de
/// configuração e mapeie sem ambiguidade para uma tabela de dispersão.
/// </para>
/// <para>
/// A promessa é cumprida na leitura e cobra o preço na <b>recusa</b>. Ler
/// <c>chave = valor</c> é fácil; o difícil é decidir quando uma tabela pode
/// ser estendida, e é aí que estão dois terços da suíte oficial.
/// </para>
/// </remarks>
public sealed partial class Analisador
{
    private readonly string _texto;
    private int _posicao;
    private int _linha = 1;
    private int _coluna = 1;

    private readonly Valor.Tabela _raiz = new();
    private Valor.Tabela _atual;

    private Analisador(string texto)
    {
        _texto = texto;
        _atual = _raiz;
    }

    /// <summary>Lê um documento TOML inteiro.</summary>
    /// <exception cref="ErroDeToml">quando o documento não é válido.</exception>
    public static Valor.Tabela Analisar(string texto)
    {
        var analisador = new Analisador(Preparar(texto));
        analisador.Documento();

        return analisador._raiz;
    }

    /// <summary>
    /// Lê um documento a partir dos bytes — que é a forma correta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A especificação diz que <b>um documento TOML é UTF-8</b>, sem alternativa
    /// e sem negociação. Receber uma <c>string</c> já pronta é receber um
    /// documento que alguém decodificou, e quem decodificou pode ter trocado
    /// byte inválido por U+FFFD em silêncio — que é o que a maioria dos
    /// leitores faz por padrão.
    /// </para>
    /// <para>
    /// Isso não é detalhe: a suíte tem sete arquivos com UTF-8 malformado e
    /// todos têm de ser recusados. Um deles é um arquivo em UTF-16, que em
    /// UTF-8 não quer dizer nada. Com a decodificação frouxa, os sete viram
    /// texto com losangos dentro e passam.
    /// </para>
    /// </remarks>
    public static Valor.Tabela Analisar(ReadOnlySpan<byte> bytes)
    {
        string texto;

        try
        {
            texto = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException erro)
        {
            throw new ErroDeToml(
                $"o arquivo nao e UTF-8 valido: {erro.Message}", 1, 1);
        }

        return Analisar(texto);
    }

    /// <summary>Lê um documento, devolvendo <c>null</c> em vez de estourar.</summary>
    public static Valor.Tabela? TentarAnalisar(string texto, out ErroDeToml? erro)
    {
        try
        {
            erro = null;

            return Analisar(texto);
        }
        catch (ErroDeToml falha)
        {
            erro = falha;

            return null;
        }
    }

    /// <summary>
    /// Tira a marca de ordem de bytes do começo, se houver.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Um editor do Windows grava UTF-8 com três bytes de prefixo, e eles
    /// chegam aqui como U+FEFF. A especificação não diz nada sobre isso; a
    /// suíte tem um arquivo com BOM e espera que ele seja aceito, porque
    /// recusá-lo faria o formato inutilizável em metade das máquinas do mundo.
    /// </para>
    /// <para>
    /// <b>Uma</b> marca, e só a primeira. Duas seguidas são erro, e a suíte tem
    /// o arquivo: a segunda não é prefixo de nada, é um caractere invisível no
    /// meio do documento. O mesmo vale para uma marca depois do <c>=</c>.
    /// </para>
    /// </remarks>
    private static string Preparar(string texto) =>
        texto.Length > 0 && texto[0] == '\uFEFF' ? texto[1..] : texto;

    // ------------------------------------------------------------------
    // O cursor
    // ------------------------------------------------------------------

    private bool Acabou => _posicao >= _texto.Length;

    private char Atual => _texto[_posicao];

    private char Espiar(int adiante = 1) =>
        _posicao + adiante < _texto.Length ? _texto[_posicao + adiante] : '\0';

    private bool Comeca(string prefixo) =>
        string.CompareOrdinal(_texto, _posicao, prefixo, 0, prefixo.Length) == 0
        && _posicao + prefixo.Length <= _texto.Length;

    private char Avancar()
    {
        var caractere = _texto[_posicao++];

        if (caractere == '\n')
        {
            _linha++;
            _coluna = 1;
        }
        else
        {
            _coluna++;
        }

        return caractere;
    }

    private ErroDeToml Erro(string mensagem) => new(mensagem, _linha, _coluna);

    private ErroDeToml ErroEm(int linha, int coluna, string mensagem) =>
        new(mensagem, linha, coluna);

    // ------------------------------------------------------------------
    // Estrutura do documento
    // ------------------------------------------------------------------

    private void Documento()
    {
        while (true)
        {
            PularEspacoEComentarios();

            if (Acabou)
            {
                return;
            }

            if (Atual == '[')
            {
                Cabecalho();
            }
            else
            {
                ParDeChaveEValor();
            }

            ExigirFimDeLinha();
        }
    }

    /// <summary>Espaço, comentários e quebras de linha entre as coisas.</summary>
    private void PularEspacoEComentarios()
    {
        while (!Acabou)
        {
            if (Atual is ' ' or '\t')
            {
                Avancar();
            }
            else if (Atual == '\n')
            {
                Avancar();
            }
            else if (Atual == '\r')
            {
                // Um `\r` solto não é quebra de linha. Aceitar um faria o
                // arquivo ser lido diferente conforme o sistema que o gravou,
                // que é exatamente o que um formato de configuração não pode.
                if (Espiar() != '\n')
                {
                    throw Erro("retorno de carro sem a quebra de linha depois");
                }

                Avancar();
            }
            else if (Atual == '#')
            {
                Comentario();
            }
            else
            {
                return;
            }
        }
    }

    private void PularEspaco()
    {
        while (!Acabou && Atual is ' ' or '\t')
        {
            Avancar();
        }
    }

    private void Comentario()
    {
        Avancar(); // o #

        while (!Acabou && Atual != '\n')
        {
            if (Atual == '\r' && Espiar() == '\n')
            {
                return;
            }

            var caractere = Atual;

            // Comentário também não aceita caractere de controle. O motivo é o
            // mesmo de sempre: um byte invisível no meio de um arquivo de
            // configuração é um jeito de esconder coisa.
            if (EhControleProibido(caractere))
            {
                throw Erro($"caractere de controle U+{(int)caractere:X4} num comentario");
            }

            Avancar();
        }
    }

    private static bool EhControleProibido(char caractere) =>
        (caractere < ' ' && caractere != '\t') || caractere == '\u007F';

    private void ExigirFimDeLinha()
    {
        PularEspaco();

        if (!Acabou && Atual == '#')
        {
            Comentario();
        }

        if (Acabou)
        {
            return;
        }

        if (Atual == '\n')
        {
            Avancar();

            return;
        }

        if (Atual == '\r' && Espiar() == '\n')
        {
            Avancar();
            Avancar();

            return;
        }

        throw Erro($"esperava fim de linha e veio {Mostrar(Atual)}");
    }

    private static string Mostrar(char caractere) =>
        caractere switch
        {
            '\n' => "quebra de linha",
            '\r' => "retorno de carro sem a quebra de linha depois",
            '\t' => "tabulacao",
            _ when EhControleProibido(caractere) => $"U+{(int)caractere:X4}",
            _ => $"'{caractere}'",
        };

    // ------------------------------------------------------------------
    // Cabeçalhos: [tabela] e [[array de tabelas]]
    // ------------------------------------------------------------------

    private void Cabecalho()
    {
        var linha = _linha;
        var coluna = _coluna;

        Avancar(); // o primeiro [

        var ehArray = !Acabou && Atual == '[';

        if (ehArray)
        {
            Avancar();
        }

        PularEspaco();

        var caminho = Caminho();

        PularEspaco();

        if (Acabou || Atual != ']')
        {
            throw Erro("esperava ] fechando o cabecalho");
        }

        Avancar();

        if (ehArray)
        {
            if (Acabou || Atual != ']')
            {
                throw Erro("esperava ]] fechando o cabecalho de array de tabelas");
            }

            Avancar();
        }

        _atual = ehArray
            ? AbrirArrayDeTabelas(caminho, linha, coluna)
            : AbrirTabela(caminho, linha, coluna);
    }

    private Valor.Tabela AbrirTabela(List<string> caminho, int linha, int coluna)
    {
        var tabela = DescerAte(caminho[..^1], linha, coluna, porChavePontilhada: false);
        var ultima = caminho[^1];

        if (!tabela.TentarObter(ultima, out var existente))
        {
            var nova = new Valor.Tabela { DefinidaPorCabecalho = true };
            tabela.Por(ultima, nova);

            return nova;
        }

        if (existente is not Valor.Tabela alvo)
        {
            throw ErroEm(linha, coluna,
                $"'{string.Join('.', caminho)}' ja e um valor de outro tipo");
        }

        // Uma tabela que nasceu implicitamente — o `a` de `[a.b]` — pode ser
        // definida depois, **uma vez**. Uma que já tem cabeçalho, não.
        if (alvo.DefinidaPorCabecalho)
        {
            throw ErroEm(linha, coluna,
                $"a tabela '{string.Join('.', caminho)}' ja foi definida");
        }

        if (alvo.EmLinha)
        {
            throw ErroEm(linha, coluna,
                $"'{string.Join('.', caminho)}' foi escrita entre chaves e esta fechada");
        }

        if (alvo.CriadaPorChavePontilhada)
        {
            throw ErroEm(linha, coluna,
                $"'{string.Join('.', caminho)}' foi criada por chave pontilhada e nao "
                + "pode ganhar um cabecalho");
        }

        alvo.DefinidaPorCabecalho = true;
        alvo.CriadaImplicitamente = false;

        return alvo;
    }

    private Valor.Tabela AbrirArrayDeTabelas(List<string> caminho, int linha, int coluna)
    {
        var tabela = DescerAte(caminho[..^1], linha, coluna, porChavePontilhada: false);
        var ultima = caminho[^1];

        Valor.Lista lista;

        if (!tabela.TentarObter(ultima, out var existente))
        {
            lista = new Valor.Lista();
            tabela.Por(ultima, lista);
        }
        else if (existente is Valor.Lista achada)
        {
            // Uma lista escrita com colchetes é um valor, e não um lugar onde
            // pendurar tabelas. `a = []` seguido de `[[a]]` é erro.
            if (achada.EscritaComColchetes)
            {
                throw ErroEm(linha, coluna,
                    $"'{string.Join('.', caminho)}' e uma lista escrita com colchetes");
            }

            lista = achada;
        }
        else
        {
            throw ErroEm(linha, coluna,
                $"'{string.Join('.', caminho)}' ja e um valor de outro tipo");
        }

        var nova = new Valor.Tabela
        {
            DefinidaPorCabecalho = true,
            ElementoDeArrayDeTabelas = true,
        };

        lista.Acrescentar(nova);

        return nova;
    }

    /// <summary>
    /// Desce pelo caminho, criando o que faltar — e é aqui que o array de
    /// tabelas complica.
    /// </summary>
    /// <remarks>
    /// Quando o caminho atravessa uma lista, a descida continua no
    /// <b>último</b> elemento dela. É o que faz
    /// <c>[[frutas]]</c> seguido de <c>[frutas.variedade]</c> acrescentar a
    /// variedade à fruta que acabou de nascer, e não à primeira.
    /// </remarks>
    private Valor.Tabela DescerAte(
        IReadOnlyList<string> caminho, int linha, int coluna, bool porChavePontilhada)
    {
        var atual = _atual;
        atual = _raiz;

        for (var i = 0; i < caminho.Count; i++)
        {
            var parte = caminho[i];

            if (!atual.TentarObter(parte, out var existente))
            {
                var nova = new Valor.Tabela { CriadaImplicitamente = true };
                atual.Por(parte, nova);
                atual = nova;

                continue;
            }

            atual = existente switch
            {
                Valor.Tabela tabela when tabela.EmLinha => throw ErroEm(linha, coluna,
                    $"'{string.Join('.', caminho.Take(i + 1))}' foi escrita entre chaves "
                    + "e esta fechada"),
                Valor.Tabela tabela => tabela,
                Valor.Lista { Quantos: > 0 } lista
                    when !lista.EscritaComColchetes && lista.Itens[^1] is Valor.Tabela ultima
                    => ultima,
                _ => throw ErroEm(linha, coluna,
                    $"'{string.Join('.', caminho.Take(i + 1))}' ja e um valor de outro tipo"),
            };
        }

        return atual;
    }

    // ------------------------------------------------------------------
    // Pares de chave e valor
    // ------------------------------------------------------------------

    private void ParDeChaveEValor()
    {
        var linha = _linha;
        var coluna = _coluna;

        var caminho = Caminho();

        PularEspaco();

        if (Acabou || Atual != '=')
        {
            throw Erro("esperava = depois da chave");
        }

        Avancar();
        PularEspaco();

        var valor = LerValor();

        Guardar(_atual, caminho, valor, linha, coluna);
    }

    private void Guardar(
        Valor.Tabela raiz, List<string> caminho, Valor valor, int linha, int coluna)
    {
        var atual = raiz;

        // As partes do meio de uma chave pontilhada criam tabelas — e elas
        // ficam marcadas, porque depois disso um cabeçalho `[a]` sobre elas é
        // erro.
        for (var i = 0; i < caminho.Count - 1; i++)
        {
            var parte = caminho[i];

            if (!atual.TentarObter(parte, out var existente))
            {
                var nova = new Valor.Tabela { CriadaPorChavePontilhada = true };
                atual.Por(parte, nova);
                atual = nova;

                continue;
            }

            if (existente is not Valor.Tabela tabela)
            {
                throw ErroEm(linha, coluna,
                    $"'{string.Join('.', caminho.Take(i + 1))}' ja e um valor de outro tipo");
            }

            if (tabela.EmLinha)
            {
                throw ErroEm(linha, coluna,
                    $"'{string.Join('.', caminho.Take(i + 1))}' foi escrita entre chaves "
                    + "e esta fechada");
            }

            // Estender por chave pontilhada uma tabela que ganhou cabeçalho
            // próprio é erro: `[a.b]` depois `a.b.c = 1` escrito de fora.
            if (tabela.DefinidaPorCabecalho && !ReferenceEquals(tabela, raiz))
            {
                throw ErroEm(linha, coluna,
                    $"'{string.Join('.', caminho.Take(i + 1))}' ja tem cabecalho proprio");
            }

            atual = tabela;
        }

        var ultima = caminho[^1];

        if (atual.Tem(ultima))
        {
            throw ErroEm(linha, coluna, $"a chave '{string.Join('.', caminho)}' ja foi definida");
        }

        atual.Por(ultima, valor);
    }

    /// <summary>Uma chave, possivelmente pontilhada.</summary>
    private List<string> Caminho()
    {
        var partes = new List<string> { UmaChave() };

        while (true)
        {
            PularEspaco();

            if (Acabou || Atual != '.')
            {
                return partes;
            }

            Avancar();
            PularEspaco();
            partes.Add(UmaChave());
        }
    }

    private string UmaChave()
    {
        if (Acabou)
        {
            throw Erro("esperava uma chave e o arquivo acabou");
        }

        if (Atual == '"')
        {
            return TextoBasico();
        }

        if (Atual == '\'')
        {
            return TextoLiteral();
        }

        var inicio = _posicao;

        while (!Acabou && EhCaractereDeChaveNua(Atual))
        {
            Avancar();
        }

        if (_posicao == inicio)
        {
            throw Erro($"esperava uma chave e veio {Mostrar(Atual)}");
        }

        return _texto[inicio.._posicao];
    }

    private static bool EhCaractereDeChaveNua(char caractere) =>
        caractere is >= 'A' and <= 'Z'
        or >= 'a' and <= 'z'
        or >= '0' and <= '9'
        or '_' or '-';

    // ------------------------------------------------------------------
    // Valores
    // ------------------------------------------------------------------

    private Valor LerValor()
    {
        if (Acabou)
        {
            throw Erro("esperava um valor e o arquivo acabou");
        }

        return Atual switch
        {
            '"' or '\'' => new Valor.Texto(TextoQualquer()),
            '[' => Lista(),
            '{' => TabelaEmLinha(),
            't' or 'f' => Logico(),
            _ => NumeroOuMomento(),
        };
    }

    private Valor Logico()
    {
        if (Comeca("true"))
        {
            for (var i = 0; i < 4; i++)
            {
                Avancar();
            }

            return new Valor.Logico(true);
        }

        if (Comeca("false"))
        {
            for (var i = 0; i < 5; i++)
            {
                Avancar();
            }

            return new Valor.Logico(false);
        }

        throw Erro("esperava um valor");
    }

    private Valor.Lista Lista()
    {
        Avancar(); // o [

        var lista = new Valor.Lista { EscritaComColchetes = true };

        while (true)
        {
            // Dentro de colchetes, quebra de linha e comentário são só espaço.
            // É a diferença entre a lista e a tabela em linha, que tem de
            // caber numa linha só.
            PularEspacoEComentarios();

            if (Acabou)
            {
                throw Erro("a lista nao foi fechada");
            }

            if (Atual == ']')
            {
                Avancar();

                return lista;
            }

            lista.Acrescentar(LerValor());

            PularEspacoEComentarios();

            if (Acabou)
            {
                throw Erro("a lista nao foi fechada");
            }

            if (Atual == ',')
            {
                Avancar();

                continue;
            }

            if (Atual == ']')
            {
                Avancar();

                return lista;
            }

            throw Erro($"esperava , ou ] na lista e veio {Mostrar(Atual)}");
        }
    }

    private Valor.Tabela TabelaEmLinha()
    {
        Avancar(); // o {

        var tabela = new Valor.Tabela { EmLinha = true };

        PularEspaco();

        if (!Acabou && Atual == '}')
        {
            Avancar();

            return tabela;
        }

        while (true)
        {
            PularEspaco();

            var linha = _linha;
            var coluna = _coluna;
            var caminho = Caminho();

            PularEspaco();

            if (Acabou || Atual != '=')
            {
                throw Erro("esperava = dentro da tabela em linha");
            }

            Avancar();
            PularEspaco();

            Guardar(tabela, caminho, LerValor(), linha, coluna);

            PularEspaco();

            if (Acabou)
            {
                throw Erro("a tabela em linha nao foi fechada");
            }

            if (Atual == ',')
            {
                Avancar();
                PularEspaco();

                // Vírgula antes da chave de fechamento não existe no TOML 1.0.
                // A regra parece arbitrária e não é: ela vem de o formato
                // querer que a tabela em linha seja um valor, e valores não
                // crescem linha a linha.
                if (!Acabou && Atual == '}')
                {
                    throw Erro("virgula sobrando antes do } da tabela em linha");
                }

                continue;
            }

            if (Atual == '}')
            {
                Avancar();

                return tabela;
            }

            throw Erro($"esperava , ou }} na tabela em linha e veio {Mostrar(Atual)}");
        }
    }

    // ------------------------------------------------------------------
    // Texto
    // ------------------------------------------------------------------

    private string TextoQualquer()
    {
        if (Atual == '"')
        {
            return Comeca("\"\"\"") ? TextoBasicoDeVariasLinhas() : TextoBasico();
        }

        return Comeca("'''") ? TextoLiteralDeVariasLinhas() : TextoLiteral();
    }

    private string TextoBasico()
    {
        Avancar(); // a aspa

        var saida = new StringBuilder();

        while (true)
        {
            if (Acabou || Atual == '\n')
            {
                throw Erro("o texto entre aspas nao foi fechado");
            }

            var caractere = Atual;

            if (caractere == '"')
            {
                Avancar();

                return saida.ToString();
            }

            if (caractere == '\\')
            {
                Escape(saida, deVariasLinhas: false);

                continue;
            }

            if (EhControleProibido(caractere))
            {
                throw Erro($"caractere de controle U+{(int)caractere:X4} dentro do texto");
            }

            saida.Append(Avancar());
        }
    }

    private string TextoBasicoDeVariasLinhas()
    {
        for (var i = 0; i < 3; i++)
        {
            Avancar();
        }

        var saida = new StringBuilder();

        PularPrimeiraQuebra();

        while (true)
        {
            if (Acabou)
            {
                throw Erro("o texto de varias linhas nao foi fechado");
            }

            if (Comeca("\"\"\""))
            {
                Avancar();
                Avancar();
                Avancar();

                // Até duas aspas a mais fazem parte do conteúdo: `""""` é uma
                // aspa seguida do fechamento. A regra existe para dar como
                // escrever uma aspa colada no fim sem escape.
                var extras = 0;

                while (!Acabou && Atual == '"' && extras < 2)
                {
                    saida.Append(Avancar());
                    extras++;
                }

                if (!Acabou && Atual == '"')
                {
                    throw Erro("tres aspas seguidas dentro do texto de varias linhas");
                }

                return saida.ToString();
            }

            var caractere = Atual;

            if (caractere == '\\')
            {
                Escape(saida, deVariasLinhas: true);

                continue;
            }

            if (caractere == '\r')
            {
                if (Espiar() != '\n')
                {
                    throw Erro("retorno de carro sem a quebra de linha depois");
                }

                Avancar();
                saida.Append('\n');
                Avancar();

                continue;
            }

            if (caractere != '\n' && EhControleProibido(caractere))
            {
                throw Erro($"caractere de controle U+{(int)caractere:X4} dentro do texto");
            }

            saida.Append(Avancar());
        }
    }

    private string TextoLiteral()
    {
        Avancar(); // a apóstrofe

        var inicio = _posicao;

        while (true)
        {
            if (Acabou || Atual == '\n')
            {
                throw Erro("o texto entre apostrofes nao foi fechado");
            }

            if (Atual == '\'')
            {
                var conteudo = _texto[inicio.._posicao];
                Avancar();

                return conteudo;
            }

            if (EhControleProibido(Atual))
            {
                throw Erro($"caractere de controle U+{(int)Atual:X4} dentro do texto");
            }

            Avancar();
        }
    }

    private string TextoLiteralDeVariasLinhas()
    {
        for (var i = 0; i < 3; i++)
        {
            Avancar();
        }

        PularPrimeiraQuebra();

        var saida = new StringBuilder();

        while (true)
        {
            if (Acabou)
            {
                throw Erro("o texto literal de varias linhas nao foi fechado");
            }

            if (Comeca("'''"))
            {
                Avancar();
                Avancar();
                Avancar();

                var extras = 0;

                while (!Acabou && Atual == '\'' && extras < 2)
                {
                    saida.Append(Avancar());
                    extras++;
                }

                if (!Acabou && Atual == '\'')
                {
                    throw Erro("tres apostrofes seguidas dentro do texto literal");
                }

                return saida.ToString();
            }

            if (Atual == '\r')
            {
                if (Espiar() != '\n')
                {
                    throw Erro("retorno de carro sem a quebra de linha depois");
                }

                Avancar();
                saida.Append('\n');
                Avancar();

                continue;
            }

            if (Atual != '\n' && EhControleProibido(Atual))
            {
                throw Erro($"caractere de controle U+{(int)Atual:X4} dentro do texto");
            }

            saida.Append(Avancar());
        }
    }

    /// <summary>
    /// Uma quebra de linha logo depois da abertura não faz parte do texto.
    /// </summary>
    /// <remarks>
    /// É o que permite escrever o conteúdo começando na linha de baixo, que é
    /// como uma pessoa escreveria — e sem isso todo texto de várias linhas
    /// começaria com um <c>\n</c> que ninguém pôs ali.
    /// </remarks>
    private void PularPrimeiraQuebra()
    {
        if (!Acabou && Atual == '\n')
        {
            Avancar();
        }
        else if (!Acabou && Atual == '\r' && Espiar() == '\n')
        {
            Avancar();
            Avancar();
        }
    }

    private void Escape(StringBuilder saida, bool deVariasLinhas)
    {
        Avancar(); // a barra

        if (Acabou)
        {
            throw Erro("barra invertida no fim do arquivo");
        }

        var caractere = Atual;

        switch (caractere)
        {
            case 'b': Avancar(); saida.Append('\b'); return;
            case 't': Avancar(); saida.Append('\t'); return;
            case 'n': Avancar(); saida.Append('\n'); return;
            case 'f': Avancar(); saida.Append('\f'); return;
            case 'r': Avancar(); saida.Append('\r'); return;
            case '"': Avancar(); saida.Append('"'); return;
            case '\\': Avancar(); saida.Append('\\'); return;
            case 'u': Avancar(); saida.Append(PontoDeCodigo(4)); return;
            case 'U': Avancar(); saida.Append(PontoDeCodigo(8)); return;
        }

        // A barra no fim da linha, só no texto de várias linhas: ela come a
        // quebra e todo o espaço até o próximo caractere que não é espaço. É o
        // que deixa quebrar uma linha longa no fonte sem quebrá-la no valor.
        if (deVariasLinhas && (caractere is ' ' or '\t' or '\n' or '\r'))
        {
            var viuQuebra = false;

            while (!Acabou)
            {
                if (Atual is ' ' or '\t')
                {
                    Avancar();
                }
                else if (Atual == '\n')
                {
                    viuQuebra = true;
                    Avancar();
                }
                else if (Atual == '\r' && Espiar() == '\n')
                {
                    viuQuebra = true;
                    Avancar();
                    Avancar();
                }
                else
                {
                    break;
                }
            }

            if (!viuQuebra)
            {
                throw Erro("barra invertida seguida de espaco, sem quebra de linha");
            }

            return;
        }

        throw Erro($"escape desconhecido: \\{caractere}");
    }

    private string PontoDeCodigo(int digitos)
    {
        var inicio = _posicao;

        for (var i = 0; i < digitos; i++)
        {
            if (Acabou || !Uri.IsHexDigit(Atual))
            {
                throw Erro($"o escape unicode precisa de {digitos} digitos hexadecimais");
            }

            Avancar();
        }

        // `long`, e nao `int`: `\\UFFFFFFFF` sao oito digitos hexadecimais, e
        // eles nao cabem num inteiro de 32 bits com sinal. Ler como `int` faz o
        // analisador estourar com um erro de biblioteca em vez de recusar o
        // arquivo com a razao certa -- e a suite tem esse arquivo.
        var numero = long.Parse(_texto[inicio.._posicao], NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);

        // Um substituto não é um caractere: ele só existe como metade de um par
        // em UTF-16. Escrevê-lo sozinho produz um texto que não é texto, e a
        // especificação manda recusar.
        if (numero is >= 0xD800 and <= 0xDFFF)
        {
            throw Erro($"U+{numero:X4} e metade de um par substituto");
        }

        if (numero > 0x10FFFF)
        {
            throw Erro($"U+{numero:X} esta acima do maior ponto de codigo do Unicode");
        }

        return char.ConvertFromUtf32((int)numero);
    }
}
