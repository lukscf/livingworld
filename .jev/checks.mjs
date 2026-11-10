// Checagens do Jev do LivingWorld (ServUO, era T2A). Guia: lukscf/tank4, ops/jev-kit/GUIA.md.
// O mundo depende de imersão: fala nova de NPC não pode soar moderna nem falar de "servidor".
import { noul, pass, presets, unsure, warn, no } from './jev.mjs';

// Código acha as falas novas (só o que o jogador lê: Say, SayTo, Emote, PublicOverheadMessage);
// o Jev só julga cada uma. SendMessage fica de fora: é mensagem de comando para a staff.
const FALA = /\b(?:Say|SayTo|Emote|PublicOverheadMessage)\s*\([^;]*?\$?"((?:[^"\\]|\\.){8,})"/g;
const MAX = 40;

function falasNovas(ctx) {
  const out = [];
  // Comando de staff (…Cmd.cs, Commands/) fala com GM, não com jogador.
  for (const f of ctx.changedMatching(['**/*.cs']).filter(ctx.exists).filter((f) => !/Cmd\.cs$|(^|\/)Commands?\//i.test(f)))
    for (const m of ctx.added(f).matchAll(FALA)) {
      const t = m[1].replace(/\{[^}]*\}/g, '…').trim(); // {nome} interpolado vira reticências
      if (t && !out.some((x) => x.text === t)) out.push({ file: f, text: t });
    }
  return out.slice(0, MAX);
}

export default (ctx) => {
  const falas = falasNovas(ctx);
  const ids = falas.map((f, i) => [`l${i + 1}`, f]);
  return [
    ...presets.all(ctx),
    falas.length && {
      name: 'falas-de-npc-cabem-no-mundo',
      files: ['**/*.cs'],
      state: {
        setting: 'Ultima Online in the T2A era: a medieval fantasy world of swords, magic, taverns, guards and gold coins. NPC lines are in Portuguese.',
        lines: Object.fromEntries(ids.map(([k, f]) => [k, f.text])),
      },
      questions: Object.fromEntries(ids.map(([k]) => [k, noul(
        `Would a person living in the world described in \`setting\` plausibly say \`lines.${k}\`?`,
        'The line fits that world: daily life, trade, danger, magic, rumours, or talking to a traveller.',
        'The line mentions modern technology such as phones, cars or the internet, or talks about the game as software, such as servers, lag or bugs.')])),
      decide(a) {
        const fora = ids.filter(([k]) => no(a[k])).map(([, f]) => `"${f.text}" (${f.file})`);
        const duvida = ids.filter(([k]) => unsure(a[k])).map(([, f]) => `"${f.text}"`);
        // Começa como aviso; vira reprovação depois de calibrado com `jev-kit probe`.
        if (fora.length) return warn(`fala parece fora do mundo: ${fora.join('; ')}`);
        if (duvida.length) return warn(`fala talvez fora do mundo: ${duvida.join('; ')}`);
        return pass();
      },
    },
  ];
};
