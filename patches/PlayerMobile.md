# Patch: `Scripts/Mobiles/PlayerMobile.cs`

O único ponto em que o LivingWorld toca o core do ServUO. Duas linhas.

Verificado contra os arquivos de core do ServUO: `BaseCreature.cs`, `BaseAI.cs` e
`Server/Mobile.cs` têm **zero** referências ao LW. Só `PlayerMobile.cs` precisa disto.

## 1. O `using`

Junto dos demais `using` no topo do arquivo:

```csharp
using Server.Custom.LivingWorld;
```

## 2. O hook de movimento

Em `OnLocationChange`, como **primeira** instrução — antes de `CheckLightLevels`:

```csharp
protected override void OnLocationChange(Point3D oldLocation)
{
    LWPassiveNav.OnPlayerMoved(this, oldLocation);   // <-- adicionar
    CheckLightLevels(false);

    DesignContext context = m_DesignContext;

    if (context == null || m_NoRecursion)
    {
        ...
```

## Por quê

`LWPassiveNav` grava passivamente por onde os jogadores realmente andam dentro dos
limites de cada cidade. Essa gravação é a matéria-prima do `LWLocalNav` — a malha de
navegação de "última milha" que os NPCs usam para se mover dentro das cidades.

Sem o hook, o resto do LivingWorld compila e roda, mas a malha intra-cidade nunca
aprende nada novo: os NPCs ficam restritos aos caminhos que já estiverem gravados
em `livingworld_recorded_paths.txt` (ou a nenhum, numa instalação zerada).
