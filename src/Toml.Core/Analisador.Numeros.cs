using System.Globalization;

namespace Toml.Core;

/// <summary>
/// A parte do analisador que lê números, datas e horas.
/// </summary>
public sealed partial class Analisador
{
    /// <summary>
    /// Decide entre número e momento, e a decisão é por espiada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>1979-05-27</c> e <c>1979</c> começam igual, e o que separa os dois é
    /// o quarto caractere adiante. Por isso a escolha não pode ser feita
    /// lendo um símbolo por vez: ela olha a forma do que vem.
    /// </para>
    /// <para>
    /// O caso mais desagradável é o separador: o TOML aceita
    /// <c>1979-05-27 07:32:00</c>, com <b>espaço</b> no lugar do <c>T</c>.
    /// Isso quer dizer que um espaço no meio de um valor às vezes faz parte
    /// dele e às vezes o termina, e a única forma de saber é olhar se o que
    /// vem depois parece uma hora.
    /// </para>
    /// </remarks>
    private Valor NumeroOuMomento()
    {
        if (PareceData())
        {
            return new Valor.Momento(LerMomentoComData());
        }

        if (PareceHora())
        {
            return new Valor.Momento(LerHoraLocal());
        }

        return LerNumero();
    }

    private bool EhDigito(int adiante) =>
        _posicao + adiante < _texto.Length && char.IsAsciiDigit(_texto[_posicao + adiante]);

    private bool EhCaractere(int adiante, char qual) =>
        _posicao + adiante < _texto.Length && _texto[_posicao + adiante] == qual;

    private bool PareceData() =>
        EhDigito(0) && EhDigito(1) && EhDigito(2) && EhDigito(3) && EhCaractere(4, '-');

    private bool PareceHora() =>
        EhDigito(0) && EhDigito(1) && EhCaractere(2, ':');

    private int LerDigitos(int quantos, string oQue)
    {
        var inicio = _posicao;

        for (var i = 0; i < quantos; i++)
        {
            if (Acabou || !char.IsAsciiDigit(Atual))
            {
                throw Erro($"{oQue} precisa de {quantos} digitos");
            }

            Avancar();
        }

        return int.Parse(_texto[inicio.._posicao], CultureInfo.InvariantCulture);
    }

    private void Exigir(char esperado, string oQue)
    {
        if (Acabou || Atual != esperado)
        {
            throw Erro($"esperava '{esperado}' {oQue}");
        }

        Avancar();
    }

    private DataHora LerMomentoComData()
    {
        var ano = LerDigitos(4, "o ano");
        Exigir('-', "depois do ano");
        var mes = LerDigitos(2, "o mes");
        Exigir('-', "depois do mes");
        var dia = LerDigitos(2, "o dia");

        ConferirData(ano, mes, dia);

        // Depois da data pode vir uma hora, e o separador é `T`, `t` ou um
        // espaço. O espaço só conta se o que vem depois dele for mesmo uma
        // hora — senão ele é o espaço que separa o valor do comentário.
        var temHora = false;

        if (!Acabou && (Atual is 'T' or 't'))
        {
            Avancar();
            temHora = true;
        }
        else if (!Acabou && Atual == ' '
                 && EhDigito(1) && EhDigito(2) && EhCaractere(3, ':'))
        {
            Avancar();
            temHora = true;
        }

        if (!temHora)
        {
            return DataHora.DeDataLocal(ano, mes, dia);
        }

        var (hora, minuto, segundo, fracao) = LerHora();
        var deslocamento = LerDeslocamento();

        return deslocamento is null
            ? DataHora.DeDataHoraLocal(ano, mes, dia, hora, minuto, segundo, fracao)
            : DataHora.DeInstante(ano, mes, dia, hora, minuto, segundo, fracao, deslocamento);
    }

    private DataHora LerHoraLocal()
    {
        var (hora, minuto, segundo, fracao) = LerHora();

        return DataHora.DeHoraLocal(hora, minuto, segundo, fracao);
    }

    private (int Hora, int Minuto, int Segundo, string Fracao) LerHora()
    {
        var hora = LerDigitos(2, "a hora");
        Exigir(':', "depois da hora");
        var minuto = LerDigitos(2, "o minuto");
        Exigir(':', "depois do minuto");
        var segundo = LerDigitos(2, "o segundo");

        if (hora > 23)
        {
            throw Erro($"hora {hora} nao existe");
        }

        if (minuto > 59)
        {
            throw Erro($"minuto {minuto} nao existe");
        }

        // Sessenta segundos existe, e não é engano: é o segundo bissexto. A
        // Terra não gira num ritmo constante, e desde 1972 o IERS acrescenta
        // um segundo ao último minuto de junho ou dezembro quando a diferença
        // passa de 0,9 s. Aconteceu 27 vezes. Recusar 60 faria o formato não
        // conseguir escrever `2016-12-31T23:59:60Z`, que é um instante que
        // existiu.
        if (segundo > 60)
        {
            throw Erro($"segundo {segundo} nao existe");
        }

        var fracao = string.Empty;

        if (!Acabou && Atual == '.')
        {
            Avancar();

            var inicio = _posicao;

            while (!Acabou && char.IsAsciiDigit(Atual))
            {
                Avancar();
            }

            if (_posicao == inicio)
            {
                throw Erro("o ponto da fracao de segundo precisa de digitos depois");
            }

            fracao = _texto[inicio.._posicao];
        }

        return (hora, minuto, segundo, fracao);
    }

    private string? LerDeslocamento()
    {
        if (Acabou)
        {
            return null;
        }

        if (Atual is 'Z' or 'z')
        {
            Avancar();

            // A suíte compara na forma canônica, e `Z` maiúsculo é a dela. O
            // arquivo pode escrever minúsculo — a gramática do RFC 3339 não
            // diferencia caixa.
            return "Z";
        }

        if (Atual is not ('+' or '-'))
        {
            return null;
        }

        var sinal = Avancar();
        var hora = LerDigitos(2, "a hora do deslocamento");
        Exigir(':', "depois da hora do deslocamento");
        var minuto = LerDigitos(2, "o minuto do deslocamento");

        if (hora > 23)
        {
            throw Erro($"deslocamento de {hora} horas nao existe");
        }

        if (minuto > 59)
        {
            throw Erro($"deslocamento de {minuto} minutos nao existe");
        }

        return $"{sinal}{hora:D2}:{minuto:D2}";
    }

    private void ConferirData(int ano, int mes, int dia)
    {
        if (mes is < 1 or > 12)
        {
            throw Erro($"mes {mes} nao existe");
        }

        var limite = DataHora.DiasNoMes(ano, mes);

        if (dia < 1 || dia > limite)
        {
            throw Erro($"{ano}-{mes:D2} tem {limite} dias, e veio o dia {dia}");
        }
    }

    // ------------------------------------------------------------------
    // Números
    // ------------------------------------------------------------------

    private Valor LerNumero()
    {
        var inicio = _posicao;
        var sinal = 1;

        if (!Acabou && (Atual is '+' or '-'))
        {
            sinal = Atual == '-' ? -1 : 1;
            Avancar();
        }

        if (Comeca("inf"))
        {
            Avancar();
            Avancar();
            Avancar();

            return new Valor.Fracionario(sinal > 0
                ? double.PositiveInfinity
                : double.NegativeInfinity);
        }

        if (Comeca("nan"))
        {
            Avancar();
            Avancar();
            Avancar();

            // O sinal de um "não é número" não significa nada, e a suíte não
            // o compara: `nan`, `+nan` e `-nan` são o mesmo valor.
            return new Valor.Fracionario(double.NaN);
        }

        // Base diferente de dez: só sem sinal, e é o que a especificação diz.
        // Um `-0xff` seria ambíguo de um jeito bobo, porque a notação
        // hexadecimal já tem como escrever negativo em complemento de dois.
        if (!Acabou && Atual == '0' && _posicao == inicio && !Acabou)
        {
            var seguinte = Espiar();

            if (seguinte is 'x' or 'o' or 'b')
            {
                Avancar();
                Avancar();

                return new Valor.Inteiro(InteiroEmBase(seguinte switch
                {
                    'x' => 16,
                    'o' => 8,
                    _ => 2,
                }));
            }
        }

        return NumeroEmBaseDez(inicio, sinal);
    }

    private long InteiroEmBase(int raiz)
    {
        var digitos = LerCorpoComSublinhados(caractere => EhDigitoDaBase(caractere, raiz));

        if (digitos.Length == 0)
        {
            throw Erro($"esperava digitos na base {raiz}");
        }

        try
        {
            return raiz switch
            {
                16 => Convert.ToInt64(digitos, 16),
                8 => Convert.ToInt64(digitos, 8),
                _ => Convert.ToInt64(digitos, 2),
            };
        }
        catch (OverflowException)
        {
            throw Erro($"o numero nao cabe em 64 bits: {digitos}");
        }
        catch (ArgumentException)
        {
            throw Erro($"digito invalido na base {raiz}: {digitos}");
        }
    }

    private static bool EhDigitoDaBase(char caractere, int raiz) => raiz switch
    {
        16 => Uri.IsHexDigit(caractere),
        8 => caractere is >= '0' and <= '7',
        _ => caractere is '0' or '1',
    };

    /// <summary>
    /// Lê dígitos com sublinhados no meio — e o sublinhado tem de estar
    /// <b>entre</b> dígitos.
    /// </summary>
    /// <remarks>
    /// <c>1_000_000</c> vale e <c>_1</c>, <c>1_</c> e <c>1__0</c> não. A regra
    /// existe para que o sublinhado seja sempre enfeite de leitura e nunca
    /// parte do número: se <c>1_</c> valesse, a diferença entre um número e
    /// uma chave nua ficaria ambígua.
    /// </remarks>
    private string LerCorpoComSublinhados(Func<char, bool> ehDigito)
    {
        var saida = new System.Text.StringBuilder();
        var ultimoFoiSublinhado = false;
        var primeiro = true;

        while (!Acabou)
        {
            if (ehDigito(Atual))
            {
                saida.Append(Avancar());
                ultimoFoiSublinhado = false;
                primeiro = false;

                continue;
            }

            if (Atual == '_')
            {
                if (primeiro)
                {
                    throw Erro("sublinhado antes do primeiro digito");
                }

                if (ultimoFoiSublinhado)
                {
                    throw Erro("dois sublinhados seguidos");
                }

                Avancar();
                ultimoFoiSublinhado = true;

                continue;
            }

            break;
        }

        if (ultimoFoiSublinhado)
        {
            throw Erro("sublinhado sem digito depois");
        }

        return saida.ToString();
    }

    private Valor NumeroEmBaseDez(int inicio, int sinal)
    {
        var parteInteira = LerCorpoComSublinhados(char.IsAsciiDigit);

        if (parteInteira.Length == 0)
        {
            throw Erro("esperava um numero");
        }

        // Zero à esquerda não vale, e a razão é o octal: em C, Python 2 e
        // JavaScript antigo, `0755` é um número em base oito. O TOML recusa a
        // forma em vez de escolher um significado para ela.
        if (parteInteira.Length > 1 && parteInteira[0] == '0')
        {
            throw Erro($"zero a esquerda em {parteInteira}");
        }

        var temFracao = !Acabou && Atual == '.';
        var temExpoente = !Acabou && (Atual is 'e' or 'E');

        if (!temFracao && !temExpoente)
        {
            var texto = (sinal < 0 ? "-" : string.Empty) + parteInteira;

            if (!long.TryParse(texto, NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var inteiro))
            {
                throw Erro($"o numero nao cabe em 64 bits: {texto}");
            }

            return new Valor.Inteiro(inteiro);
        }

        var numero = new System.Text.StringBuilder();
        numero.Append(sinal < 0 ? "-" : string.Empty).Append(parteInteira);

        if (temFracao)
        {
            Avancar();

            var decimais = LerCorpoComSublinhados(char.IsAsciiDigit);

            if (decimais.Length == 0)
            {
                throw Erro("o ponto decimal precisa de digitos depois");
            }

            numero.Append('.').Append(decimais);

            temExpoente = !Acabou && (Atual is 'e' or 'E');
        }

        if (temExpoente)
        {
            Avancar();
            numero.Append('e');

            if (!Acabou && (Atual is '+' or '-'))
            {
                numero.Append(Avancar());
            }

            // O expoente **pode** ter zero à esquerda: `1e01` vale. A regra do
            // octal não se aplica aqui porque um expoente nunca foi escrito em
            // base oito em lugar nenhum.
            var expoente = LerCorpoComSublinhados(char.IsAsciiDigit);

            if (expoente.Length == 0)
            {
                throw Erro("o expoente precisa de digitos");
            }

            numero.Append(expoente);
        }

        return new Valor.Fracionario(double.Parse(
            numero.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture));
    }
}
