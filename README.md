# LivingWorld

Um mundo vivo para shards [ServUO](https://github.com/ServUO/ServUO) na era T2A/pré-AOS.

Não é um pacote de spawner. É uma simulação persistente: ~2.200 cidadãos com
rotina própria, economia que produz e consome itens reais, memória de longo prazo
sobre o jogador, controle de território que muda de mão, e um "GM invisível" que
injeta narrativa quando detecta crise ou dominância no estado do mundo.

**27.500 linhas de C#, 59 arquivos, 22 comandos in-game.** Sem LLM: todo o diálogo
é léxico e roda local.

> ServUO é o emulador. Este repositório é só o que foi construído **em cima** dele —
> o LivingWorld não é um fork do ServUO, e sim uma camada que se instala por cima
> de uma árvore limpa. O acoplamento com o core é de **duas linhas** (ver [Instalação](#instalação)).

---

## O que faz do mundo "vivo"

Sistemas de simulação, não de spawn. Cada um roda contínuo e escreve estado em disco.

| Sistema | O que faz |
|---|---|
| **LWManager** (249 KB) | O cérebro. Mantém a população alvo e pulsa a máquina de estados de cada cidadão: trabalho, viagem, caçada, patrulha, emboscada, duelo. Simula eventos **fora da vista** do jogador — mortes, boatos — para o mundo continuar acontecendo onde ninguém olha. |
| **LWCitizen** (270 KB) | O indivíduo: profissão, inventário, skills, estado social e de sobrevivência. |
| **LWEconomy** (60 KB) | Economia pré-AOS de todas as profissões, batendo com os vendors reais. Cada profissão **cria ou coleta seu item de verdade** — não é número abstrato. |
| **LWDialogue** (89 KB) | Cérebro de conversa, 100% léxico. ~22 tópicos com sinônimos, gírias e **erros de escrita** tolerados. Sem chamada de rede. |
| **LWTerritory** | Camada A-Life estilo STALKER: o controle do mapa muda com o tempo **e com o jogador**. Acampamento de PK que o jogador limpou não volta igual. |
| **LWPlayerMemory** | Memória persistente do mundo sobre cada jogador (estilo Nemesis + Radiant AI), em disco, keyed pelo `Serial`. |
| **LWNarrativeEngine** | GM invisível: lê o estado do mundo e injeta eventos coerentes quando detecta crises ou dominâncias. |
| **LWLore** | Histórico persistente do servidor — o que aconteceu vira lenda que NPCs contam **meses depois**. |
| **LWSurvivalMemory** | Um bot de cada tipo aprende sobre a própria categoria e replica o aprendizado aos demais. |
| **LWSocialMemory** | Memória de relacionamento NPC-com-NPC: quem brigou com quem fica brigado. |
| **LWFame** | Fama emergente — a lenda nasce do que o NPC fez, não de um slot pré-autorado. |
| **LWSagaEvents** (43 KB) | Eventos temáticos de larga escala. |
| **LWWild** | Fauna com predadores caçando presas de verdade; mortos-vivos só à noite. |
| **LWWeather** | Clima com efeito mecânico, não flavor text. |

### Navegação — o problema difícil

Pathfinding dentro de cidade é onde shards com NPCs ambulantes quebram. A solução
aqui não é algoritmo, é **dado observado**:

- **LWPassiveNav** grava passivamente por onde qualquer jogador anda dentro dos limites de uma cidade.
- **LWLocalNav** constrói a malha de "última milha" 100% a partir de trechos que alguém **andou de verdade** — é a única fonte de andabilidade intra-cidade.
- **LWPathEdit** é o editor visual in-game dessa malha (GM).
- **LWNavHealth** é telemetria + auto-diagnóstico: detecta focos de travamento e classifica.
- **LWNavGump** lista os focos e teleporta o GM direto pra cada um.

### Interface

- **LWGump** — painel de controle em 5 abas: Status / Systems / Events / Nav / Admin.
- **LWDialogGump** — diálogo clicável estilo Skyrim, em 3 modos: perguntas, comércio, missões.
- **LWSpeech** + **LWChatter** — 500+ falas ambiente por tipo de bot, PT-BR sem acentos (limitação do chat do UO).

---

## Addons (`src/Addons/`)

Sistemas construídos sobre o LW, cada um independente do outro:

| | |
|---|---|
| **CityLords** | Um Lord por cidade guardada: 1000 HP, máscara tingida na cor da cidade, patrulha banco/taverna/forja/moongate. Escoltado por 5 guardas + 4 Nightmares vinculados em `ControlOrder.Guard` — reaproveita o `DoOrderGuard` nativo do ServUO, o mesmo mecanismo de qualquer pet de jogador. |
| **GateFavors** | Pedir `Gate to Britain please!` no chat público tem 10% de chance de alguém por perto atender — ou de uma voz sem dono responder, se não houver ninguém. |
| **VendorMalls** | Simula o clássico "gate no banco pro meu vendor": 4 lojas temáticas + um pregoeiro ao lado do banco. |
| **WorldEvents** | Eventos aleatórios **sem hora marcada** — um timer interno sorteia. |
| **FactionWar** | As 4 facções (Minax, Council of Mages, True Britannians, Shadowlords) em guerra aberta na Felucca. |

---

## Instalação

Sobre uma árvore ServUO limpa:

**1.** Copie o conteúdo de `src/` para `Scripts/Custom/`:

```bash
cp -r src/LivingWorld src/Addons/*.cs /caminho/do/servuo/Scripts/Custom/
```

O ServUO compila `Scripts/` recursivamente — o layout de pastas não importa para a
compilação, só para a sua organização.

**2.** Aplique o patch de 2 linhas em `Scripts/Mobiles/PlayerMobile.cs` — ver
[`patches/PlayerMobile.md`](patches/PlayerMobile.md). É o **único** ponto em que o
LivingWorld toca o core do ServUO.

**3.** Compile e suba. No primeiro boot o `LWManager` popula o mundo; use `[lw` para
abrir o painel.

### A dependência de `legacy/`

`LWLegacy.cs` existe para aposentar a geração anterior de bots estáticos: as classes
continuam compiladas (saves antigos ainda as referenciam), mas nada mais as spawna e
o purge remove as instâncias no boot.

Ele faz isso com uma lista de 26 `typeof()`, então **`src/LivingWorld/` não compila
sem os 6 arquivos de bot antigos** em `legacy/` (`WorldNPCs`, `MoreNPCs`,
`PlayerBots`, `FactionNPCs`, `BankScene`, `WarNPCs`). Duas saídas:

- **Instalação limpa:** copie também esses 6 de `legacy/`. Nada será spawnado.
- **Sem o rabo legado:** remova `LWLegacy.cs` (ou troque os `typeof()` por resolução
  de tipo pelo nome, via `ScriptCompiler.FindTypeByName`). Só faz falta se você
  estiver migrando de um shard que já rodou os bots antigos.

`LWLegacy.cs` é o **único** dos 34 arquivos do core que toca esses tipos. Os outros 33
não têm dependência nenhuma fora do ServUO stock.

---

## Comandos

| Comando | Nível | |
|---|---|---|
| `[lw` | GM | Painel de controle (5 abas) |
| `[lwwild` | GM | Vida selvagem |
| `[lords` | GM | City Lords |
| `[malls` | GM | Vendor Malls |
| `[we` | GM | World Events |
| `[factionwar` | GM | Guerra de facções |
| `[gonpc` / `[findnpcs` / `[listnpcs` | GM | Diagnóstico de NPCs |
| `[repopulate` / `[repopulate2` / `[repopbanks` / `[repopwarzones` | GM | Repopulação |
| `[placechamps` / `[activatechamps` / `[listchamps` | Admin | Champion spawns T2A |
| `[spawncamps` | Admin | Acampamentos selvagens |
| `[cleanghosts` / `[resyncall` | GM | Limpa entidades presas em `Map.Internal`, força resync |
| `[gobritain` | Player | Ida rápida a Britain |
| `[tpplayer` | Admin | Teleporte de jogador |

## Persistência

Estado sobrevive a restart em arquivos-texto na raiz do servidor:

```
livingworld_player_memory.txt      memória sobre cada jogador
livingworld_territory.txt          controle de território
livingworld_lore.txt               histórico/lendas do servidor
livingworld_fame.txt               fama emergente
livingworld_social_memory.txt      relações NPC-NPC
livingworld_survival_knowledge.txt aprendizado por categoria
livingworld_recorded_paths.txt     malha de navegação gravada
livingworld_learned_paths.txt      caminhos consolidados
livingworld_deleted_via_points.txt pontos removidos no editor
livingworld_settings.txt           configuração
livingworld_status.txt             status corrente
livingworld_debug.txt              debug
```

## Estrutura do repositório

```
src/LivingWorld/   34 arquivos — o core da simulação
src/Addons/         5 arquivos — CityLords, GateFavors, VendorMalls, WorldEvents, FactionWar
legacy/            17 arquivos — bots aposentados (6, exigidos por LWLegacy)
                                 + world setup e spawners da geração anterior (11)
shard/              3 arquivos — específicos do shard de origem, não instale sem ler
patches/                        — o patch de 2 linhas no PlayerMobile.cs
```

`shard/` contém `OptusNPC.cs` (ponte HTTP para um bot externo em `127.0.0.1:5050`),
`SpawnOptusCmd.cs` e `AutoFixPosition.cs` (hook temporário com nome de personagem
hardcoded). Estão versionados por completude, mas não fazem parte do pacote.

---

## Licença

[GPL-2.0](LICENSE) — a mesma do [ServUO](https://github.com/ServUO/ServUO), do qual
este código é obra derivada: ele herda das classes do emulador (`BaseCreature`,
`BaseAI`, `PlayerMobile`) e é compilado dentro do binário do servidor.

Livre para todos, de propósito. Você pode usar, estudar, modificar e redistribuir.
A única contrapartida é a recíproca: se distribuir uma versão modificada, ela também
sai sob GPL-2.0, com o fonte junto. Ninguém pode fechar este mundo.

Copyright © 2026 Lucas Cruz Ferreira. Licenciar não é ceder autoria — o LivingWorld
continua sendo meu trabalho, e a licença é o que garante que ele siga sendo de todos.
