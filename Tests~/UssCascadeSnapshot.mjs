import { createHash } from 'node:crypto';

export function snapshot(source) {
    const rules = [...source.replace(/\/\*[\s\S]*?\*\//g, '').matchAll(/([^{}]+)\{([^{}]*)\}/g)].map(m => ({
        selectors: m[1].split(',').map(s => s.trim().replace(/\s+/g, ' ')
            .replace('.unity-base-popup-field__arrow.whimtex-effect-target-arrow', '.whimtex-effect-target .unity-base-popup-field__arrow')
            .replace('.unity-base-popup-field__text.whimtex-effect-target-text', '.whimtex-effect-target .unity-base-popup-field__text')),
        declarations: m[2].split(';').map(d => d.trim()).filter(Boolean).map(d => {
            const colon = d.indexOf(':');
            if (colon < 0) throw Error(`Invalid declaration: ${d}`);
            return [d.slice(0, colon).trim(), d.slice(colon + 1).trim().replace(/\s+/g, ' ')];
        })
    }));
    const theme = rules.find(r => r.selectors.length === 1 && r.selectors[0] === '.whimtex-theme');
    const tokens = new Map(theme?.declarations ?? []);
    const properties = new Map();
    for (const rule of rules) {
        if (rule === theme) continue;
        for (const [property, raw] of rule.declarations) {
            const value = raw.replace(/var\((--whimtex-[\w-]+)\)/g, (_, token) => {
                if (!tokens.has(token)) throw Error(`Missing palette token: ${token}`);
                return tokens.get(token);
            });
            const chain = properties.get(property) ?? [];
            let group = chain.at(-1);
            if (group?.value !== value) chain.push(group = {value, selectors: new Set()});
            rule.selectors.forEach(selector => group.selectors.add(selector));
            properties.set(property, chain);
        }
    }
    return Object.fromEntries([...properties].sort(([a], [b]) => a.localeCompare(b)).map(([property, chain]) => {
        const canonical = chain.map(g => [g.value, [...g.selectors].sort()]);
        return [property, createHash('sha256').update(JSON.stringify(canonical)).digest('hex')];
    }));
}
