// Editor-independent mirror of ShaderFXMetadata's declaration rules. Unity still
// resolves assets, validates generated uniforms and compiles the actual shader.
const types = new Set(['float', 'bool', 'enum', 'float2', 'float3', 'float4', 'normal',
  'point', 'color', 'texture2D', 'transform2D', 'gradient', 'curve']);
const identifier = /^[A-Za-z_][A-Za-z0-9_]*$/;
const numeric = /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/;
const scalar = type => ['float', 'bool', 'enum'].includes(type);
const compatible = (a, b) => a === b || scalar(a) && scalar(b) ||
  (a === 'point' && b === 'float2') || (a === 'float2' && b === 'point');
const fail = message => { throw new Error(message); };

function number(text, double = false) {
  text = text.trim();
  const value = double ? Number(text) : Math.fround(Number(text));
  if (!numeric.test(text) || !Number.isFinite(value)) fail(`Expected a finite number: ${text}.`);
  return value;
}

function tuple(text, count, double = false) {
  if (!text.startsWith('(') || !text.endsWith(')')) fail('Expected components in parentheses.');
  const parts = text.slice(1, -1).split(',');
  if (parts.length !== count) fail(`Expected ${count} components.`);
  return parts.map(part => number(part, double));
}

function color(text) {
  text = text.trim();
  if (text.startsWith('#')) {
    if (!/^#[\da-fA-F]{6}(?:[\da-fA-F]{2})?$/.test(text)) fail('Expected #RRGGBB or #RRGGBBAA.');
  } else tuple(text, 4);
}

function curve(text) {
  if (['linear', 'one', 'easeIn', 'easeOut', 'easeInOut'].includes(text)) return;
  if (!text.startsWith('keys(') || !text.endsWith(')'))
    fail('Expected linear, one, easeIn, easeOut, easeInOut or keys((seven values), ...).');
  const body = text.slice(5, -1);
  let end = 0, count = 0, previous = -Infinity;
  for (const key of body.matchAll(/\(([^()]*)\)/g)) {
    if (body.slice(end, key.index).trim() !== (count ? ',' : '')) fail('Invalid curve key separator.');
    const parts = key[1].split(',').map(p => p.trim());
    if (parts.length !== 7) fail('Curve keys require seven values.');
    const values = parts.map((p, i) => (i === 2 || i === 3) && /^(?:inf|-inf)$/.test(p)
      ? (p === 'inf' ? Infinity : -Infinity) : number(p));
    if (values[0] <= previous) fail('Curve times must be strictly increasing.');
    if (values[4] < 0 || values[4] > 1 || values[5] < 0 || values[5] > 1 ||
        !Number.isInteger(values[6]) || values[6] < 0 || values[6] > 3)
      fail('Curve weights must be within 0..1; weightedMode must be 0, 1, 2 or 3.');
    previous = values[0];
    if (++count > 256) fail('A curve may contain at most 256 keys.');
    end = key.index + key[0].length;
  }
  if (body.slice(end).trim()) fail('Invalid curve keys.');
}

function transform(text) {
  if (!text.startsWith('matrix(')) { tuple(text, 5, true); return; }
  let m = tuple(text.slice(6), 9, true);
  if (m[8] === 0) fail('Transform matrix must have a nonzero bottom-right component.');
  m = m.map(v => v / m[8]);
  if (m.some(v => !Number.isFinite(v))) fail('Normalized transform matrix must be finite.');
  const co = [m[4]*m[8]-m[5]*m[7], m[2]*m[7]-m[1]*m[8], m[1]*m[5]-m[2]*m[4],
    m[5]*m[6]-m[3]*m[8], m[0]*m[8]-m[2]*m[6], m[2]*m[3]-m[0]*m[5],
    m[3]*m[7]-m[4]*m[6], m[1]*m[6]-m[0]*m[7], m[0]*m[4]-m[1]*m[3]];
  const determinant = m[0]*co[0]+m[1]*co[3]+m[2]*co[6];
  const corners = [m[8], m[6]+m[8], m[6]+m[7]+m[8], m[7]+m[8]];
  const limit = Math.max(...corners.map(Math.abs)) * 1e-7;
  if (!Number.isFinite(determinant) || Math.abs(determinant) < 1e-20 ||
      co.some(v => !Number.isFinite(v / determinant)) ||
      !(corners.every(v => v > limit) || corners.every(v => v < -limit)))
    fail('Transform matrix must be invertible with no horizon crossing its rectangle.');
}

// Parentheses/quotes in labels must not be mistaken for a tooltip separator.
function stripTooltip(text) {
  let quoted = false, depth = 0;
  for (let i = 0; i < text.length - 1; i++) {
    if (quoted && text[i] === '\\') { i++; continue; }
    if (text[i] === '"') quoted = !quoted;
    if (quoted) continue;
    if (text[i] === '(') depth++;
    if (text[i] === ')' && depth > 0) depth--;
    if (!depth && text.slice(i, i + 2) === '//') return text.slice(0, i).trimEnd();
  }
  return text.trimEnd();
}

function modifiers(text) {
  let hidden = false, label = null;
  for (;;) {
    text = text.trimStart();
    if (/^hidden(?:\s|$)/.test(text)) {
      if (hidden) fail('The hidden modifier may be specified only once.');
      hidden = true; text = text.slice(6); continue;
    }
    if (!/^label(?:\s|\(|$)/.test(text)) break;
    if (label !== null) fail('The label modifier may be specified only once.');
    text = text.slice(5).trimStart();
    if (text[0] !== '(') fail('Expected label text in label(...).');
    let depth = 1, quoted = false, end = 1;
    for (; end < text.length; end++) {
      const c = text[end];
      if (quoted && c === '\\') { end++; continue; }
      if (c === '"') quoted = !quoted;
      if (!quoted && c === '(') depth++;
      if (!quoted && c === ')' && --depth === 0) break;
    }
    if (depth || quoted) fail('Unterminated label(...).');
    label = text.slice(1, end).trim();
    if (label.startsWith('"') && label.endsWith('"')) label = label.slice(1, -1).replace(/\\([\\"])/g, '$1');
    if (!label.trim()) fail('The label modifier must contain non-empty text.');
    text = text.slice(end + 1);
  }
  return { text, hidden };
}

function parseParameter(body) {
  const { text: modified, hidden } = modifiers(body);
  const text = stripTooltip(modified);
  const head = text.match(/^([A-Za-z0-9]+)\s+([A-Za-z_][A-Za-z0-9_]*)(.*)$/);
  if (!head || !types.has(head[1])) fail('Expected a supported @param type and parameter name.');
  const [, type, name] = head;
  let rest = head[3].trim(), value;
  if (type === 'enum') {
    const match = rest.match(/^(?:=\s*([^{}]+?))?\s*\{([^{}]+)\}$/);
    if (!match) fail('Expected enum options {Name: number, ...}, without a semicolon.');
    const options = new Map(), values = new Set();
    for (const item of match[2].split(',')) {
      const option = item.trim().match(/^([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.+)$/);
      if (!option) fail('Enum items must be Name: number.');
      const n = number(option[2]);
      if (options.has(option[1]) || values.has(n)) fail('Enum names and values must be unique.');
      options.set(option[1], n); values.add(n);
    }
    if (match[1] !== undefined && !options.has(match[1].trim())) number(match[1]);
    return { type, name, hidden };
  }
  let range;
  const rangeStart = rest.indexOf('[');
  if (rangeStart >= 0) {
    const match = rest.slice(rangeStart).match(/^\[\s*(.*?)\s*\.\.\s*(.*?)\s*\]$/);
    if (!match) fail('Expected a range [min .. max].');
    range = match.slice(1); rest = rest.slice(0, rangeStart).trim();
  }
  if (rest) {
    if (!rest.startsWith('=') || !rest.slice(1).trim() || /[\[\];~]/.test(rest))
      fail('Expected = value followed by an optional range, without a semicolon.');
    value = rest.slice(1).trim();
  }
  if (range && type !== 'float') fail('Ranges apply only to float parameters.');
  if (type === 'float') {
    const initial = value === undefined ? undefined : number(value);
    if (range) {
      const soft = range.map(p => p.trim().startsWith('~'));
      const bounds = range.map((p, i) => (soft[i] ? p.trim().slice(1) : p).trim());
      const [min, max] = bounds.map(p => p ? number(p) : undefined);
      if (min === undefined && max === undefined) fail('A range needs at least one boundary.');
      if (min > max) fail('Minimum exceeds maximum.');
      if (soft.some(Boolean) && !(min !== undefined && max !== undefined && min < max))
        fail('Soft ranges require two finite values with min < max; put ~ before the number.');
      if (initial !== undefined && ((!soft[0] && initial < min) || (!soft[1] && initial > max)))
        fail('Default is outside the declared hard range.');
    }
  } else if (value !== undefined) {
    switch (type) {
      case 'bool': if (!/^(true|false|0|1)$/.test(value)) fail('Expected true, false, 0 or 1 for bool.'); break;
      case 'float2': case 'float3': case 'float4': case 'normal': case 'point': {
        const count = {float2: 2, float3: 3, float4: 4, normal: 3, point: 2}[type];
        const components = tuple(value, count);
        if (type === 'point' && components.some(v => v < 0 || v > 1)) fail('Point coordinates must be within 0..1.');
        break;
      }
      case 'color': color(value); break;
      case 'gradient': {
        const endpoints = value.split('->');
        if (endpoints.length !== 2) fail('Expected two colors separated by ->.');
        endpoints.forEach(color); break;
      }
      case 'curve': curve(value); break;
      case 'transform2D': transform(value); break;
      case 'texture2D': {
        if (value === 'self' || value === 'none') break;
        const reference = value.match(/^"guid:([\da-fA-F]{32}):(-?\d+)"$/);
        if (!reference || BigInt(reference[2]) < -(1n << 63n) || BigInt(reference[2]) >= (1n << 63n))
          fail('Expected self, none or "guid:<32-digit GUID>:<64-bit local file ID>".');
        break;
      }
    }
  }
  return { type, name, hidden };
}

// Match Unity's line-oriented metadata scanner, ignoring commented-out examples.
function directiveLines(source) {
  const result = [];
  let block = false;
  const lines = source.split(/\r\n|\n|\r/);
  for (let line = 0; line < lines.length; line++) {
    const text = lines[line];
    if (!block) {
      const match = text.match(/^\s*\/\/\s*@\s*([A-Za-z][A-Za-z0-9-]*)(.*)$/);
      if (match) result.push({ line, start: text.indexOf('@'), length: text.length, name: match[1], body: match[2].trim(), text });
    }
    for (let i = 0; i < text.length - 1; i++) {
      if (block) { if (text.slice(i, i + 2) === '*/') { block = false; i++; } }
      else if (text.slice(i, i + 2) === '//') break;
      else if (text.slice(i, i + 2) === '/*') { block = true; i++; }
      else if (text[i] === '"') {
        for (i++; i < text.length; i++) {
          if (text[i] === '\\') i++;
          else if (text[i] === '"') break;
        }
      }
    }
  }
  return result;
}

function validate(source) {
  const diagnostics = [], names = new Map(), declarations = [], groups = [], conditions = [];
  const directives = directiveLines(source);
  let group = null, condition = null, aliases = [], headers = 0, helpBoxes = 0;
  const report = (d, message, warning = false) => diagnostics.push({ line: d.line, start: d.start, end: d.length, message, warning });
  for (const d of directives) {
    const { name, body } = d;
    try {
      switch (name) {
        case 'whimtex-effect':
          if (d.line !== 0 || !/^\uFEFF?\/\/\s*@whimtex-effect\s+/.test(d.text))
            report(d, 'The catalog marker is recognized only on the first line, without indentation.', true);
          if (!body || body.split('/').some(p => !p.trim())) fail('Effect category/name must not contain empty segments.');
          break;
        case 'param': {
          const p = { ...parseParameter(body), d, group, condition, aliases };
          if (aliases.includes(p.name)) fail('A parameter cannot list its current name as a former name.');
          const existing = names.get(p.name);
          if (existing && existing.some(other => !compatible(other.type, p.type)))
            fail(`Conflicting storage types for ${p.name}.`);
          if (!existing) names.set(p.name, [p]); else existing.push(p);
          declarations.push(p);
          if (group) group.parameters.push(p);
          if (declarations.length > 128) report(d, 'At most 128 parameter controls are supported.');
          aliases = []; headers = 0; helpBoxes = 0;
          break;
        }
        case 'if': {
          if (condition) fail('Nested @if blocks are not supported.');
          const match = body.match(/^([A-Za-z_][A-Za-z0-9_]*)\s*(==|!=)\s*(\S+)$/);
          if (!match) fail('Expected @if _Parameter == number or @if _Parameter != number.');
          number(match[3]); condition = { name: match[1], d }; conditions.push(condition); break;
        }
        case 'endif':
          if (body) fail('@endif takes no arguments.');
          if (!condition) fail('@endif has no matching @if.');
          condition = null; aliases = []; headers = 0; helpBoxes = 0; break;
        case 'group': {
          if (group) fail('Nested @group blocks are not supported.');
          if (condition) fail('@group must be outside conditional blocks.');
          const match = body.match(/^\((.*)\)$/);
          if (body && (!match || !match[1].trim())) fail('Expected @group, @group(Title), or @group(Title; _Parameter).');
          const parts = match ? match[1].split(';').map(p => p.trim()) : [];
          if (parts.length > 2 || parts.length && !parts[0] || parts.length === 2 && !identifier.test(parts[1]))
            fail('Expected a non-empty title and a valid parameter name after the semicolon.');
          group = { d, header: parts[1], parameters: [] }; groups.push(group); break;
        }
        case 'endgroup':
          if (body) fail('@endgroup takes no arguments.');
          if (!group) fail('@endgroup has no matching @group.');
          if (condition) fail('Conditional block must end before @endgroup.');
          group = null; aliases = []; headers = 0; helpBoxes = 0; break;
        case 'formerlyserializedas': {
          const match = body.match(/^\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)$/);
          if (!match) fail('Expected @formerlyserializedas(_OldName).');
          if (aliases.length >= 16) fail('A parameter may have at most 16 former names.');
          if (aliases.includes(match[1])) fail(`Duplicate former name ${match[1]}.`);
          aliases.push(match[1]); break;
        }
        case 'header': case 'helpbox': {
          const match = body.match(/^\((.*)\)$/);
          if (!match || !match[1].trim()) fail(`Expected @${name}(non-empty text).`);
          if ((name === 'header' ? ++headers : ++helpBoxes) > 128) fail(`Too many consecutive @${name} directives.`);
          break;
        }
        default: report(d, `Unknown WhimTex directive @${name}.`, true);
      }
    } catch (error) { report(d, error.message); }
  }
  if (condition) report(condition.d, '@if has no matching @endif.');
  if (group) report(group.d, '@group has no matching @endgroup.');
  if (aliases.length) {
    const last = directives.at(-1);
    report(last, '@formerlyserializedas must be followed by a parameter declaration.');
  }
  for (const g of groups) {
    if (!g.parameters.length) report(g.d, '@group must contain at least one parameter.');
    else if (!g.parameters.some(p => !p.hidden)) report(g.d, '@group must contain at least one visible parameter.');
    if (g.header) {
      const controls = names.get(g.header);
      if (!controls) report(g.d, `Group header parameter ${g.header} is not declared in the group.`);
      else if (controls.length !== 1) report(g.d, `Group header parameter ${g.header} must be declared once.`);
      else if (controls[0].group !== g || controls[0].condition)
        report(g.d, `Group header parameter ${g.header} must be unconditional and inside the group.`);
    }
  }
  const owners = new Map();
  for (const p of declarations) {
    for (const alias of p.aliases) {
      if (names.has(alias) && alias !== p.name || owners.has(alias) && owners.get(alias) !== p.name)
        report(p.d, `Former name ${alias} is used or claimed by another parameter.`);
      owners.set(alias, p.name);
    }
  }
  for (const c of conditions) {
    // Like Unity, conditions attached to no declarations have no dependencies.
    if (!declarations.some(p => p.condition === c)) continue;
    const controls = names.get(c.name);
    if (!controls) report(c.d, `@if refers to undeclared parameter ${c.name}.`);
    else if (!scalar(controls[0].type)) report(c.d, '@if can reference only float, bool or enum parameters.');
    else if (controls.some(p => p.condition)) report(c.d, '@if must reference an unconditional parameter.');
  }
  return diagnostics;
}

module.exports = { validate, parseParameter, directiveLines };
