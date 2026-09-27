# toml

Um analisador de **TOML 1.0.0** escrito do zero em C# e .NET 8 — sem
dependência nenhuma fora do xUnit.

O juiz é o **toml-test**, a suíte oficial que o próprio grupo do TOML mantém e
que toda implementação usa para dizer que é conforme:

**709 de 709 arquivos.** E a proporção entre eles diz o que é difícil no
formato:

| | |
|---|---|
| **208** válidos, lidos e comparados com o JSON esperado | ✓ |
| **501** inválidos, **recusados** | ✓ |

```bash
dotnet test -c Release

dotnet build -c Release
dotnet src/Toml.Cli/bin/Release/net8.0/Toml.Cli.dll suite
```

## Dois terços da suíte são arquivos errados

Isso não é desequilíbrio: é onde está a dificuldade do formato.

Ler `chave = valor` é fácil. O difícil é saber **quando uma tabela pode ser
estendida** — e cada um desses 501 arquivos é um jeito diferente de errar isso.

```
$ toml conferir config.toml

  invalido — linha 5, coluna 12
  zero a esquerda em 01

  5 | limite = 01
    |            ^
```

Um analisador que recusa os 501 dizendo "arquivo inválido" passa num teste de
exceção sem ter entendido nada. E quem escreve um `pyproject.toml` errado às
onze da noite precisa da linha, não de um "erro de sintaxe".

## Por que o TOML existe

Ele nasceu em 2013 de uma frase do Tom Preston-Werner: um formato de
configuração "óbvio e mínimo". A razão é que o **INI nunca foi especificado** —
cada programa lê o seu de um jeito, e não há como saber se `a=1` tem espaços ou
se `#` começa um comentário — e o **YAML é grande demais** para caber na cabeça
de alguém. (O YAML tem nove formas de escrever uma string e uma cláusula que
transforma `NO` em `false`, o que já derrubou a configuração de mais de um país
com código ISO `no`.)

A promessa do TOML é que o arquivo se leia como configuração e mapeie **sem
ambiguidade** para uma tabela de dispersão. A promessa é cumprida na leitura, e
cobra o preço na recusa.

## Os quatro tipos que nenhum outro formato tem

```
$ toml tipos

    instante = 1979-05-27T07:32:00Z   datetime
    local    = 1979-05-27T07:32:00   datetime-local
    dia      = 1979-05-27   date-local
    hora     = 07:32:00   time-local
```

JSON não tem data. Quem precisa de uma escreve uma string e combina com o outro
lado qual é o formato, o que nunca funciona.

O TOML tem quatro, e a diferença entre eles é **semântica**, não de escrita:

- **Instante** é um ponto na linha do tempo. Dois instantes com deslocamentos
  diferentes podem ser o mesmo ponto.
- **Data e hora local** não é um ponto na linha do tempo até alguém dizer onde.
  É o que uma agenda guarda para "a reunião das 9h" — se o horário de verão
  mudar, a reunião continua às 9h.
- **Data local** é um aniversário.
- **Hora local** é um horário de abertura.

A validação é por conta própria, e não pelo `DateTimeOffset.Parse` do .NET. O
motivo: aquele método aceita coisas que o TOML recusa (`1979-5-27`, com mês de
um dígito) e o TOML aceita coisas que ele não representa (uma hora local sem
data). Emprestar a validação de um tipo de outra biblioteca é como emprestar a
régua de outro desenho.

E `23:59:60` é válido. É o **segundo bissexto**: a Terra não gira num ritmo
constante, e desde 1972 o IERS acrescentou 27 deles. `2016-12-31T23:59:60Z` é
um instante que existiu.

## Quando uma tabela pode crescer

Uma tabela pode ter nascido de quatro jeitos, e cada um a deixa aberta a coisas
diferentes:

| nasceu de | aceita | não aceita |
|---|---|---|
| `[a]` | chaves | um segundo `[a]` |
| o `a` de `[a.b]` | um `[a]` depois, **uma** vez | dois |
| o `a` de `a.b = 1` | outras chaves pontilhadas | um cabeçalho `[a]` |
| `{ }` | nada | nada — fechada no instante em que a chave fecha |

```toml
[a.b]      # cria `a` implicitamente
x = 1

[a]        # válido: `a` ainda não tinha cabeçalho
y = 2

[a]        # INVÁLIDO: agora tem
```

```toml
[pomar]
fruta.cor = "vermelha"

[pomar.fruta]   # INVÁLIDO: `fruta` nasceu de chave pontilhada
```

A regra da chave pontilhada existe para que a ordem de leitura do arquivo seja
a ordem de escrita dele: um `[pomar.fruta]` mais abaixo faria o leitor voltar
atrás.

E a tabela em linha está fechada porque ela é um **valor**, não um lugar. Valores
não crescem linha a linha.

## As decisões que parecem arbitrárias e não são

**Zero à esquerda é erro.** Em C, Python 2 e JavaScript antigo, `0755` é um
número em base oito. O TOML recusa a forma em vez de escolher um significado
para ela.

**O sublinhado tem de estar entre dígitos.** `1_000_000` vale, `1_` não. Assim
ele é sempre enfeite de leitura e nunca parte do número — se `1_` valesse, a
fronteira entre um número e uma chave nua ficaria ambígua.

**O expoente pode ter zero à esquerda.** `1e01` vale, porque expoente nunca foi
escrito em base oito em lugar nenhum.

**Um `\r` sozinho é erro.** Aceitar um faria o arquivo ser lido diferente
conforme o sistema que o gravou, que é exatamente o que um formato de
configuração não pode.

**Vírgula antes do `}` não existe** — ao contrário do `]`, onde ela vale. A
assimetria vem de a tabela em linha ser um valor de uma linha só.

**Caractere de controle não entra nem no comentário.** Um byte invisível no
meio de um arquivo de configuração é um jeito de esconder coisa.

## Um documento TOML é UTF-8, e ponto

A API principal recebe **bytes**, e não `string`:

```csharp
Analisador.Analisar(File.ReadAllBytes("config.toml"));
```

Receber uma `string` já pronta é receber um documento que alguém decodificou —
e quem decodificou pode ter trocado byte inválido por U+FFFD em silêncio, que é
o que a maioria dos leitores faz por padrão.

Isso não é detalhe: sete arquivos da suíte têm UTF-8 malformado e todos têm de
ser recusados. Um deles é um arquivo em **UTF-16**, que em UTF-8 não quer dizer
nada. Com a decodificação frouxa, os sete viram texto com losangos dentro e
passam.

E a marca de ordem de bytes: **uma** passa, duas não. A segunda não é prefixo de
nada — é um caractere invisível no meio do documento. A suíte tem esse arquivo,
e ele foi o mais difícil de acertar, porque o `File.ReadAllText` do .NET já
tinha comido a primeira antes de o código ver o arquivo.

## A codificação que a suíte compara

```
$ toml json config.toml

{
  "titulo": {
    "type": "string",
    "value": "CondeClub"
  },
  "servidor": {
    "porta": {
      "type": "integer",
      "value": "8080"
    }
  }
}
```

Parece redundante e é a única forma de o teste funcionar. O JSON tem **um** tipo
numérico e o TOML tem dois: sem a etiqueta, `1` e `1.0` ficariam
indistinguíveis, e um analisador que lesse todo número como `double` passaria
na suíte. E o `value` é sempre string porque um inteiro de 64 bits não cabe no
número do JSON sem perder dígito.

A comparação é **estrutural**, e não de texto: a ordem das chaves de um objeto
não significa nada, e `1e3` e `1000.0` são o mesmo número. Comparar strings
faria o teste reprovar acertos. Cada tipo decide como comparar — `float` como
número (com `nan` igual a `nan`, que é a única igualdade que o IEEE 754 nega),
`integer` como inteiro de 64 bits, o resto como texto exato.

Há uma armadilha nisso, e ela tem teste próprio: uma tabela TOML pode ter
exatamente as chaves `type` e `value`, e aí ela **não** é uma folha.

## Rodando

```bash
dotnet test -c Release

dotnet build -c Release
dotnet src/Toml.Cli/bin/Release/net8.0/Toml.Cli.dll ver config.toml
dotnet src/Toml.Cli/bin/Release/net8.0/Toml.Cli.dll conferir config.toml
dotnet src/Toml.Cli/bin/Release/net8.0/Toml.Cli.dll json config.toml
dotnet src/Toml.Cli/bin/Release/net8.0/Toml.Cli.dll tipos
dotnet src/Toml.Cli/bin/Release/net8.0/Toml.Cli.dll suite
```

```
$ toml ver config.toml

titulo = "CondeClub"   string
versao = "1.4.0"   string
servidor
  porta  = 8080   integer
  limite = 2500   integer
  prazo  = 1979-05-27T07:32:00Z   datetime
rotas
  [0]
    caminho = "/entrar"   string
    metodos = ["GET", "POST"]   array
  [1]
    caminho = "/sair"   string
    metodos = ["POST"]   array
```

Como biblioteca:

```csharp
using Toml.Core;

var tabela = Analisador.Analisar(File.ReadAllBytes("config.toml"));

tabela["titulo"];                       // Valor.Texto
((Valor.Tabela)tabela["servidor"])["porta"];

// Sem estourar:
if (Analisador.TentarAnalisar(texto, out var erro) is { } lido) { }
else Console.WriteLine($"linha {erro!.Linha}: {erro.Explicacao}");
```

## Estrutura

```
src/Toml.Core/Analisador.cs          a descida recursiva, e as regras de tabela
src/Toml.Core/Analisador.Numeros.cs  numeros nas quatro bases, datas e horas
src/Toml.Core/Valor.cs               os oito tipos, e as bandeiras de cada tabela
src/Toml.Core/DataHora.cs            RFC 3339 a mao, e a forma canonica
src/Toml.Core/ParaJson.cs            a codificacao etiquetada da suite
src/Toml.Cli/Program.cs              ver, json, conferir, tipos, suite
tests/Toml.Tests/suite/              os 709 arquivos, como vieram
```

## Limites conhecidos

- **Só lê.** Não há como gravar TOML a partir de uma tabela. Escrever é outro
  problema, e um bom: decidir entre `[a.b]` e `a.b = {}` para cada nó é uma
  escolha de estilo que muda o arquivo inteiro.
- **Só o TOML 1.0.0.** A versão 1.1.0 está em rascunho e acrescenta segundos
  opcionais na hora, escapes `\e` e `\x`, e chaves nuas com caractere não-ASCII.
  A suíte já traz a lista `files-toml-1.1.0`, e este projeto usa a de 1.0.0.
- **O documento inteiro vai para a memória.** Um `Cargo.lock` de dez megabytes
  cabe; um arquivo de um gigabyte não passa por aqui.
- **Sem posição de origem nos valores.** O erro traz linha e coluna, mas um
  valor lido com sucesso não sabe de onde veio — o que seria a primeira coisa a
  acrescentar para usar isto num editor.
- **Sem conversão para tipos do .NET.** Não há `Deserializar<T>`. O que sai é a
  árvore de `Valor`, e ligá-la a uma classe é outro projeto, com reflexão
  dentro.

## Onde ele se encaixa

Faz par com o
[`analisador-json`](https://github.com/condeDeveloper/analisador-json), que lê o
outro formato de dados da coleção e também se importa mais com o erro do que com
o acerto, e com o [`fuso-horario`](https://github.com/condeDeveloper/fuso-horario),
que resolve o que um `datetime-local` do TOML deixa em aberto: **onde**.

## Licença

MIT, para o código.

Os arquivos em `tests/Toml.Tests/suite/` são do
[toml-test](https://github.com/toml-lang/toml-test), sob a licença MIT que
acompanha a pasta.
