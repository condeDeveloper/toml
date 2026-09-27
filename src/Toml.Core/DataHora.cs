using System.Globalization;
using System.Text;

namespace Toml.Core;

/// <summary>
/// Os quatro tipos de data e hora do TOML.
/// </summary>
/// <remarks>
/// <para>
/// Esta é a parte do TOML que nenhum outro formato de configuração tem, e é o
/// motivo declarado de ele existir. JSON não tem data — quem precisa de uma
/// escreve uma string e combina com o outro lado qual é o formato, o que nunca
/// funciona. O TOML tem <b>quatro</b> tipos, e a diferença entre eles é
/// semântica, não de escrita:
/// </para>
/// <list type="bullet">
///   <item><b>Instante</b> — <c>1979-05-27T07:32:00Z</c>. Um ponto na linha do
///   tempo. Dois instantes com deslocamentos diferentes podem ser o mesmo
///   ponto.</item>
///   <item><b>Data e hora local</b> — <c>1979-05-27T07:32:00</c>. Sem
///   deslocamento: não é um ponto na linha do tempo até alguém dizer onde. É o
///   que uma agenda guarda para "a reunião das 9h".</item>
///   <item><b>Data local</b> — <c>1979-05-27</c>. Um aniversário.</item>
///   <item><b>Hora local</b> — <c>07:32:00</c>. Um horário de abertura.</item>
/// </list>
/// <para>
/// A validação é por conta própria, e não pelo <c>DateTimeOffset.Parse</c> do
/// .NET. O motivo é simples: aquele método aceita coisas que o TOML recusa
/// (<c>1979-5-27</c>, com mês de um dígito) e o TOML aceita coisas que ele não
/// tem como representar (uma hora local sem data, um deslocamento de
/// <c>-00:00</c> distinto de <c>+00:00</c>). Emprestar a validação de um tipo
/// de outra biblioteca é como emprestar a régua de outro desenho.
/// </para>
/// </remarks>
public sealed record DataHora
{
    public enum Especie
    {
        /// <summary>Data, hora e deslocamento. Um ponto na linha do tempo.</summary>
        Instante,

        /// <summary>Data e hora, sem deslocamento.</summary>
        DataHoraLocal,

        /// <summary>Só a data.</summary>
        DataLocal,

        /// <summary>Só a hora.</summary>
        HoraLocal,
    }

    private DataHora(
        Especie tipo,
        int ano, int mes, int dia,
        int hora, int minuto, int segundo,
        string fracao,
        string deslocamento)
    {
        Tipo = tipo;
        Ano = ano;
        Mes = mes;
        Dia = dia;
        Hora = hora;
        Minuto = minuto;
        Segundo = segundo;
        Fracao = fracao;
        Deslocamento = deslocamento;
    }

    public Especie Tipo { get; }

    public int Ano { get; }

    public int Mes { get; }

    public int Dia { get; }

    public int Hora { get; }

    public int Minuto { get; }

    public int Segundo { get; }

    /// <summary>Os dígitos depois da vírgula, sem o ponto. Vazio se não houver.</summary>
    public string Fracao { get; }

    /// <summary>O deslocamento como escrito: <c>Z</c>, <c>+09:00</c>, ou vazio.</summary>
    public string Deslocamento { get; }

    public static DataHora DeInstante(
        int ano, int mes, int dia, int hora, int minuto, int segundo,
        string fracao, string deslocamento) =>
        new(Especie.Instante, ano, mes, dia, hora, minuto, segundo, fracao, deslocamento);

    public static DataHora DeDataHoraLocal(
        int ano, int mes, int dia, int hora, int minuto, int segundo, string fracao) =>
        new(Especie.DataHoraLocal, ano, mes, dia, hora, minuto, segundo, fracao, string.Empty);

    public static DataHora DeDataLocal(int ano, int mes, int dia) =>
        new(Especie.DataLocal, ano, mes, dia, 0, 0, 0, string.Empty, string.Empty);

    public static DataHora DeHoraLocal(int hora, int minuto, int segundo, string fracao) =>
        new(Especie.HoraLocal, 0, 0, 0, hora, minuto, segundo, fracao, string.Empty);

    /// <summary>
    /// Quantos dias tem um mês — e é aqui que mora a única regra do calendário
    /// gregoriano que as pessoas quase acertam.
    /// </summary>
    /// <remarks>
    /// Ano bissexto é divisível por 4, <b>menos</b> os divisíveis por 100,
    /// <b>mais</b> os divisíveis por 400. A exceção da exceção existe porque o
    /// ano solar tem 365,2425 dias, e não 365,25: a regra de Júlio César
    /// adiantava o calendário em um dia a cada 128 anos, e no século XVI o
    /// equinócio já tinha andado dez dias. Por isso 1900 não foi bissexto e
    /// 2000 foi — e por isso a suíte tem um teste com <c>1900-02-29</c>.
    /// </remarks>
    public static int DiasNoMes(int ano, int mes) => mes switch
    {
        1 or 3 or 5 or 7 or 8 or 10 or 12 => 31,
        4 or 6 or 9 or 11 => 30,
        2 => EhBissexto(ano) ? 29 : 28,
        _ => 0,
    };

    public static bool EhBissexto(int ano) =>
        (ano % 4 == 0 && ano % 100 != 0) || ano % 400 == 0;

    /// <summary>
    /// A forma canônica, que é a que a suíte de conformidade compara.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A suíte exige <c>T</c> maiúsculo e <c>Z</c> maiúsculo, mesmo que o
    /// arquivo traga minúsculos — o TOML aceita os dois na entrada e a
    /// comparação é feita numa forma só. É a mesma ideia de normalizar uma URL
    /// antes de compará-la.
    /// </para>
    /// <para>
    /// A fração de segundo sai com três dígitos sempre, pelo mesmo motivo:
    /// <c>.6</c> e <c>.600</c> são o mesmo instante, e a comparação precisa de
    /// uma forma só. Ver <see cref="FracaoCanonica"/>.
    /// </para>
    /// </remarks>
    public string Canonico()
    {
        var texto = new StringBuilder();

        if (Tipo is Especie.Instante or Especie.DataHoraLocal or Especie.DataLocal)
        {
            texto.Append(Ano.ToString("D4", CultureInfo.InvariantCulture))
                 .Append('-')
                 .Append(Mes.ToString("D2", CultureInfo.InvariantCulture))
                 .Append('-')
                 .Append(Dia.ToString("D2", CultureInfo.InvariantCulture));
        }

        if (Tipo is Especie.Instante or Especie.DataHoraLocal)
        {
            texto.Append('T');
        }

        if (Tipo is Especie.Instante or Especie.DataHoraLocal or Especie.HoraLocal)
        {
            texto.Append(Hora.ToString("D2", CultureInfo.InvariantCulture))
                 .Append(':')
                 .Append(Minuto.ToString("D2", CultureInfo.InvariantCulture))
                 .Append(':')
                 .Append(Segundo.ToString("D2", CultureInfo.InvariantCulture));

            if (Fracao.Length > 0)
            {
                texto.Append('.').Append(FracaoCanonica());
            }
        }

        if (Tipo == Especie.Instante)
        {
            texto.Append(Deslocamento);
        }

        return texto.ToString();
    }

    /// <summary>
    /// A fração de segundo com exatamente três dígitos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A especificação exige precisão de <b>milissegundo</b> e permite
    /// descartar o resto. Então <c>.6</c> e <c>.600</c> são o mesmo instante, e
    /// a forma canônica escolhe um dos dois para que a comparação seja
    /// possível — do mesmo jeito que a hora canoniza <c>z</c> em <c>Z</c>.
    /// </para>
    /// <para>
    /// Dígito a mais é <b>cortado</b>, e não arredondado. Arredondar mudaria o
    /// instante; cortar perde precisão que a especificação já dizia ser
    /// opcional.
    /// </para>
    /// </remarks>
    public string FracaoCanonica() =>
        Fracao.Length >= 3 ? Fracao[..3] : Fracao.PadRight(3, '0');

    /// <summary>O nome do tipo na codificação que a suíte de conformidade usa.</summary>
    public string NomeNaSuite() => Tipo switch
    {
        Especie.Instante => "datetime",
        Especie.DataHoraLocal => "datetime-local",
        Especie.DataLocal => "date-local",
        Especie.HoraLocal => "time-local",
        _ => throw new InvalidOperationException($"tipo desconhecido: {Tipo}"),
    };

    public override string ToString() => Canonico();
}
