// Editor-independent mirror of ShaderFXMetadata's declaration rules. Unity still
// resolves assets, validates generated uniforms and compiles the actual shader.
const types = new Set(['float', 'float2', 'float3', 'float4', 'normal',
  'point', 'texture2D', 'transform2D', 'gradient', 'curve']);
const identifier = /^[A-Za-z_][A-Za-z0-9_]*$/;
const numeric = /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/;
const nativeType = type => ({normal:'float3', point:'float2', toggle:'float', enum:'float', seed:'int', color:'float4'}[type] || type);
const components = type => /^(bool|int|uint|half|float)[234]?$/.test(type) ? Number(type.slice(-1)) || 1 : 0;
const compatible = (a, b) => (a.valueType || nativeType(a.type)) === (b.valueType || nativeType(b.type)) &&
  (a.type === 'color') === (b.type === 'color');
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

function previewBinding(text) {
  const parts = text.split(',').map(p => p.trim());
  if (parts.length > 129) fail('A preview may reference at most 128 parameters.');
  if (parts.some(p => !identifier.test(p)))
    fail('Preview expects a function name followed by optional comma-separated @param names, not expressions or empty arguments.');
  return { previewFunction: parts[0], previewParameters: parts.slice(1) };
}

function modifiers(text) {
  let hidden = false, rgba = false, pixelUnits = false, label = null, preview = null;
  for (;;) {
    text = text.trimStart();
    if (/^preview\b/.test(text)) {
      if (preview !== null) fail('The preview modifier may be specified only once.');
      const match = text.match(/^preview\s*\(([^()]*)\)(?=\s|$)/);
      if (!match) fail('Expected preview(FunctionName, optional parameter names).');
      preview = previewBinding(match[1]); text = text.slice(match[0].length); continue;
    }
    if (/^px(?:\s|$)/.test(text)) {
      if (pixelUnits) fail('The px modifier may be specified only once.');
      pixelUnits = true; text = text.slice(2); continue;
    }
    if (/^rgba(?:\s|$)/.test(text)) {
      if (rgba) fail('The rgba modifier may be specified only once.');
      rgba = true; text = text.slice(4); continue;
    }
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
  return { text, hidden, rgba, pixelUnits, ...(preview || {previewFunction: null, previewParameters: []}) };
}

function typedNumber(text, type) {
  const n = text.trim() === 'true' ? 1 : text.trim() === 'false' ? 0 : number(text, true);
  const family = type.replace(/[234]$/, '');
  const bounds = {bool:[0,1], int:[-2147483648,2147483647], uint:[0,4294967295], half:[-65504,65504], float:[-3.4028234663852886e38,3.4028234663852886e38]}[family];
  if (!bounds || n < bounds[0] || n > bounds[1] || ['bool','int','uint'].includes(family) && !Number.isInteger(n)) fail(`Value is outside the range of ${type}.`);
  return family === 'float' || family === 'half' ? Math.fround(n) : n;
}

function parseTyped(text, modifiers) {
  const m = text.match(/^(toggle|enum|seed|color)(?:\s*\(\s*([A-Za-z0-9]+)\s*\))?\s+([A-Za-z_]\w*)\s*(.*?)\s*$/);
  if (!m) return null;
  const [,type,explicitType,name] = m, valueType = explicitType || nativeType(type), count = components(valueType);
  if (!count || type === 'enum' && (count !== 1 || valueType === 'bool') || type === 'seed' && !['int','uint','float'].includes(valueType)) fail(`Unsupported ${type} value type: ${valueType}.`);
  if (modifiers.pixelUnits) fail('px applies only to ordinary numeric float fields.');
  if (modifiers.rgba && (count < 2 || type !== 'color' && type !== 'toggle')) fail('rgba requires a vector field or vector toggle/color.');
  let rest = m[4], options, bounds, soft;
  if (type === 'enum') {
    const e = rest.match(/^(.*?)\{([^{}]+)\}\s*$/);
    if (!e) fail('enum requires {Name: value, ...}.');
    rest = e[1].trim(); options = new Map(); const values = new Set();
    for (const item of e[2].split(',')) {
      const option = item.trim().match(/^([A-Za-z_]\w*)\s*:\s*(.+)$/);
      if (!option) fail('Enum items must be Name: number.');
      const value = typedNumber(option[2], valueType);
      if (options.has(option[1]) || values.has(value)) fail('Enum names and values must be unique.');
      options.set(option[1], value); values.add(value);
    }
  }
  if (type === 'seed') {
    bounds = valueType === 'float' ? [-16777216,16777216] : valueType === 'uint' ? [0,4294967295] : [-2147483648,2147483647];
    soft = [false,false];
    const r = rest.match(/^(.*?)\[\s*(.*?)\s*\.\.\s*(.*?)\s*\]\s*$/);
    if (r) {
      rest = r[1].trim(); soft = [r[2],r[3]].map(v => v.trim().startsWith('~'));
      bounds = [r[2],r[3]].map(v => typedNumber(v.trim().replace(/^~/,''),valueType));
    }
    if (bounds[0] > bounds[1] || bounds.some(v => !Number.isInteger(v) || valueType === 'float' && Math.abs(v) > 16777216)) fail('seed requires an ordered, exactly representable integer range.');
  }
  if (rest) {
    if (!rest.startsWith('=')) fail('Expected = default value.');
    const value = rest.slice(1).trim();
    if (type === 'color' && value.startsWith('#')) color(value);
    else if (!options?.has(value)) {
      const parts = count === 1 ? [value] : value.startsWith('(') && value.endsWith(')') ? value.slice(1,-1).split(',') : [];
      if (parts.length !== count) fail(`Expected ${count} components.`);
      for (const part of parts) {
        const v = typedNumber(part,valueType);
        if (type === 'toggle' && v !== 0 && v !== 1) fail('toggle values must be 0/1 or false/true.');
        if (type === 'seed' && (!Number.isInteger(v) || !soft[0] && v < bounds[0] || !soft[1] && v > bounds[1] || valueType === 'float' && Math.abs(v) > 16777216)) fail('seed default must be an integer inside its range.');
      }
    }
  }
  return {...modifiers, type, name, valueType, rgba:modifiers.rgba || type === 'color', seed:type === 'seed'};
}

function parseParameter(body) {
  const mods = modifiers(body);
  const { text: modified, hidden, rgba, pixelUnits, previewFunction, previewParameters } = mods;
  const text = stripTooltip(modified);
  const typed = parseTyped(text, mods);
  if (typed) return typed;
  const seed = false;
  const head = text.match(/^([A-Za-z0-9]+)\s+([A-Za-z_][A-Za-z0-9_]*)(.*)$/);
  if (!head || !types.has(head[1])) fail('Expected a supported @param type and parameter name.');
  const [, type, name] = head;
  if (rgba && !['float2','float3','float4'].includes(type)) fail('rgba requires a vector, toggle or color field.');
  if (seed && type !== 'float') fail('seed applies only to float parameters.');
  if (pixelUnits && (seed || !['float', 'float2', 'float3', 'float4'].includes(type)))
    fail('The px modifier applies only to numeric float, float2, float3 or float4 lengths, without seed.');
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
    return { type, name, hidden, pixelUnits, previewFunction, previewParameters };
  }
  let range, cyclicRange = false;
  const rangeStart = rest.indexOf('[');
  if (rangeStart >= 0) {
    const match = rest.slice(rangeStart).match(/^\[\s*(.*?)\s*(\.\.|%)\s*(.*?)\s*\]$/);
    if (!match) fail('Expected a range [min .. max] or [min % max].');
    range = [match[1], match[3]]; cyclicRange = match[2] === '%'; rest = rest.slice(0, rangeStart).trim();
  }
  if (rest) {
    if (!rest.startsWith('=') || !rest.slice(1).trim() || /[\[\];~]/.test(rest))
      fail('Expected = value followed by an optional range, without a semicolon.');
    value = rest.slice(1).trim();
  }
  if (range && type !== 'float' && type !== 'float2') fail('Ranges apply only to float and float2 parameters.');
  if (range && type === 'float2') {
    const [min, max] = range.map(p => number(p));
    if (min >= max) fail('A float2 interval needs two hard finite limits with min < max.');
    const [x, y] = value === undefined ? [min, max] : tuple(value, 2);
    const [fx, fy, fmin, fmax] = [x, y, min, max].map(Math.fround);
    const period = Math.fround(fmax - fmin);
    const full = cyclicRange && fy > fx && (fx >= fmin && fx <= fmax || fy >= fmin && fy <= fmax) &&
      Math.abs(fy - fx - period) <= Math.max(Math.abs(fx), Math.abs(fy), Math.abs(period)) / 16777216;
    if (!full && (x < min || x > max || y < min || y > max || (!cyclicRange && x > y)))
      fail(cyclicRange ? 'Cyclic endpoints must be within the limits, or one period apart with at least one endpoint within the limits.' : 'A float2 interval default must satisfy min <= x <= y <= max.');
  }
  if (type === 'float') {
    const initial = value === undefined ? undefined : number(value);
    if (range) {
      const soft = range.map(p => p.trim().startsWith('~'));
      const bounds = range.map((p, i) => (soft[i] ? p.trim().slice(1) : p).trim());
      const [min, max] = bounds.map(p => p ? number(p) : undefined);
      if (seed && (min === undefined || max === undefined || min < -16777216 || max > 16777216 || Math.ceil(min) > Math.floor(max)))
        fail('seed requires two bounds in [-16777216 .. 16777216] containing at least one integer.');
      if (min === undefined && max === undefined) fail('A range needs at least one boundary.');
      if (min > max) fail('Minimum exceeds maximum.');
      if (soft.some(Boolean) && !(min !== undefined && max !== undefined && min < max))
        fail('Soft ranges require two finite values with min < max; put ~ before the number.');
      if (cyclicRange && (soft.some(Boolean) || min === undefined || max === undefined || min >= max))
        fail('A % range needs two hard finite limits with min < max.');
      if (initial !== undefined && ((!soft[0] && initial < min) || (!soft[1] && initial > max)))
        fail('Default is outside the declared hard range.');
    } else if (seed) fail('seed requires two finite bounds.');
  } else if (value !== undefined) {
    switch (type) {
      case 'bool': if (!/^(true|false|0|1)$/.test(value)) fail('Expected true, false, 0 or 1 for bool.'); break;
      case 'float2': case 'float3': case 'float4': case 'normal': case 'point': {
        const count = {float2: 2, float3: 3, float4: 4, normal: 3, point: 2}[type];
        const components = tuple(value, count);
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
  return { type, name, hidden, rgba, seed, pixelUnits, cyclicRange, previewFunction, previewParameters };
}

// Match Unity's line-oriented metadata scanner, ignoring commented-out examples.
function directiveLines(source) {
  const result = [];
  let block = false, leading = true;
  const lines = source.split(/\r\n|\n|\r/);
  for (let line = 0; line < lines.length; line++) {
    const text = lines[line];
    if (!block) {
      const match = text.match(/^\s*\/\/\s*@\s*([A-Za-z][A-Za-z0-9-]*)(.*)$/);
      if (match) result.push({ line, start: text.indexOf('@'), length: text.length, name: match[1], body: match[2].trim(), text, leading });
    }
    for (let i = 0; i < text.length; i++) {
      if (block) { if (text.slice(i, i + 2) === '*/') { block = false; i++; } }
      else if (text.slice(i, i + 2) === '//') break;
      else if (text.slice(i, i + 2) === '/*') { block = true; i++; }
      else if (text[i] === '"') {
        leading = false;
        for (i++; i < text.length; i++) {
          if (text[i] === '\\') i++;
          else if (text[i] === '"') break;
        }
      }
      else if (/\S/.test(text[i])) leading = false;
    }
  }
  return result;
}

function parsePreset(body) {
  const match = body.match(/^\(\s*"([^"]+)"\s*,\s*([0-9]+)\s*\)$/);
  if (!match) fail('Expected // @preset("GUID", 1) with both an ID and a positive integer version.');
  const id = match[1].trim();
  if (!/^(?:[0-9a-f]{32}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$/i.test(id))
    fail('Preset ID must be a GUID in D (hyphenated) or N (32 hex digits) format.');
  const presetId = id.replace(/-/g, '').toLowerCase();
  if (/^0{32}$/.test(presetId)) fail('Preset ID must not be the empty GUID.');
  const version = Number(match[2]);
  if (!Number.isInteger(version) || version < 1 || version > 2147483647)
    fail('Preset version must be a positive integer from 1 to 2147483647.');
  return { presetId, presetVersion: version };
}

function validate(source) {
  const diagnostics = [], names = new Map(), declarations = [], containers = [], stack = [], conditions = [], conditionStack = [], previews = [];
  const directives = directiveLines(source);
  let condition = null, headers = 0, helpBoxes = 0, control = null, controlCount = 0, previewCount = 0, presetGroupCount = 0, presetCount = 0;
  const parent = () => stack[stack.length - 1];
  const nearestGroup = () => {
    for (let i = stack.length - 1; i >= 0; i--)
      if (stack[i].kind === 'group' || stack[i].kind === 'foldout') return stack[i];
  };
  const countRow = row => {
    for (const container of stack) container.parameters.push(row);
    for (const scope of conditionStack) scope.hasRow = true;
  };
  const report = (d, message, warning = false) => diagnostics.push({ line: d.line, start: d.start, end: d.length, message, warning });
  for (const d of directives) {
    const { name, body } = d;
    try {
      switch (name) {
        case 'preset':
          if (++presetCount > 1) fail('Declare only one @preset.');
          if (!d.leading) fail('Preset @preset must appear in the initial comment header before HLSL code.');
          parsePreset(body);
          break;
        case 'control': {
          const match = body.match(/^\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)$/);
          control = match ? { name: match[1], d } : null;
          if (!match) report(d, 'Expected // @control(_Parameter).', true);
          let expected = /^\uFEFF?\/\/\s*@whimtex-effect\s+/.test(source) ? 1 : 0;
          if (directives.some(item => item.line === expected && item.name === 'preset' && item.leading)) expected++;
          if (++controlCount > 1) report(d, 'Multiple @control declarations; the last declaration wins.', true);
          else if (d.line !== expected) report(d, `Place @control on line ${expected + 1}, after the effect marker and optional @preset, or first when neither exists.`, true);
          break;
        }
        case 'whimtex-effect':
          if (d.line !== 0 || !/^\uFEFF?\/\/\s*@whimtex-effect\s+/.test(d.text))
            report(d, 'The catalog marker is recognized only on the first line, without indentation.', true);
          if (!body || body.split('/').some(p => !p.trim())) fail('Effect category/name must not contain empty segments.');
          break;
        case 'icon':
          if (!/^\("[^"\r\n]+"\)$/.test(body)) report(d, 'Expected // @icon("relative/path.svg") or // @icon("guid:...").', true);
          break;
        case 'param': {
          if (parent()?.kind === 'tabs') fail('Declare parameters inside a @tab page, not directly inside @tabs.');
          const p = { ...parseParameter(body), d, container: parent(), condition,
            enableIf: conditionStack.filter(c => c.kind === 'enableif') };
          const existing = names.get(p.name);
          if (existing && existing.some(other => !compatible(other, p)))
            fail(`Conflicting storage types for ${p.name}.`);
          if (existing && existing.some(other => other.pixelUnits !== p.pixelUnits))
            fail(`Conflicting pixel units for ${p.name}.`);
          if (existing && existing.some(other => Boolean(other.cyclicRange) !== Boolean(p.cyclicRange)))
            fail(`Conflicting cyclic range declarations for ${p.name}.`);
          if (!existing) names.set(p.name, [p]); else existing.push(p);
          declarations.push(p);
          if (p.previewFunction) previews.push(p);
          countRow(p);
          if (declarations.length > 128) report(d, 'At most 128 parameter controls are supported.');
          headers = 0; helpBoxes = 0;
          break;
        }
        case 'preview': {
          if (parent()?.kind === 'tabs') fail('Declare previews inside a @tab page, not directly inside @tabs.');
          const match = body.match(/^\(([^()]*)\)$/);
          if (!match) fail('Expected // @preview(FunctionName, optional parameter names).');
          previews.push({ ...previewBinding(match[1]), d });
          if (++previewCount > 128) fail('At most 128 standalone previews are supported.');
          countRow({hidden: false});
          headers = 0; helpBoxes = 0;
          break;
        }
        case 'if': case 'enableif': {
          if (name === 'if' && condition) fail('Nested @if blocks are not supported.');
          if (conditionStack.length >= 16) fail('At most 16 condition levels are supported.');
          const match = body.match(/^([A-Za-z_][A-Za-z0-9_]*(?:\.[xyzwrgba])?)\s*(==|!=)\s*(\S+)$/);
          if (!match) fail(`Expected @${name} _Parameter == number or @${name} _Parameter != number.`);
          const scope = { kind: name, name: match[1], d, notEqual: match[2] === '!=', value: match[3] === 'true' ? 1 : match[3] === 'false' ? 0 : number(match[3],true) };
          conditions.push(scope); conditionStack.push(scope);
          if (name === 'if') condition = scope;
          break;
        }
        case 'endif': case 'endenableif': {
          if (body) fail(`@${name} takes no arguments.`);
          const kind = name.slice(3);
          if (!conditionStack.length) fail(`@${name} has no matching @${kind}.`);
          if (conditionStack[conditionStack.length - 1].kind !== kind)
            fail(`@${name} does not match the innermost open condition.`);
          conditionStack.pop();
          if (kind === 'if') condition = null;
          headers = 0; helpBoxes = 0; break;
        }
        case 'group': case 'foldout': case 'tabs': case 'tab': {
          if (name === 'group' && /^\(\s*"/.test(body)) {
            if (!d.leading) fail('Preset @group must appear in the leading comments before HLSL code.');
            const match = body.match(/^\(\s*"([^"\r\n]+)"\s*\)$/);
            if (++presetGroupCount > 1) fail('Declare only one preset @group.');
            if (!match || !match[1].trim()) fail('Expected // @group("Name") with a non-empty preset group.');
            break;
          }
          if (conditionStack.length) fail(`@${name} must be outside conditional blocks.`);
          if (stack.length >= 16) fail('At most 16 layout levels are supported.');
          if (name === 'tab') {
            if (parent()?.kind !== 'tabs') fail('@tab must be directly inside @tabs.');
          } else if (parent()?.kind === 'tabs') fail(`Place @${name} inside a @tab page, not directly inside @tabs.`);
          if (name === 'tabs' && stack.some(c => c.kind === 'tabs')) fail('Nested @tabs blocks are not supported.');
          if (name === 'group' && nearestGroup()?.kind === 'group') fail('Nested @group blocks are not supported.');
          const match = body.match(/^\((.*)\)$/);
          if (body && (!match || !match[1].trim()))
            fail(`Invalid @${name} declaration: omit parentheses for an untitled container, or provide a non-empty title.`);
          const parts = match ? match[1].split(';').map(p => p.trim()) : [];
          if (parts.length > 2 || parts.length && !parts[0] || parts.length === 2 && !identifier.test(parts[1]))
            fail('Expected a non-empty title and a valid parameter name after the semicolon.');
          if (name === 'tabs' && parts.length > 1)
            fail(`@${name} accepts only a title, not a linked parameter.`);
          if ((name === 'foldout' || name === 'tab') && !parts[0]) fail(`@${name} requires a title.`);
          if (name === 'tab') {
            if (parent().pages.has(parts[0])) fail(`Duplicate tab title ${parts[0]}.`);
            parent().pages.add(parts[0]);
          }
          const container = { d, kind: name, header: parts[1], parameters: [], pages: new Set() };
          containers.push(container); stack.push(container); break;
        }
        case 'endgroup': case 'endfoldout': case 'endtabs': case 'endtab': {
          if (body) fail(`@${name} takes no arguments.`);
          const kind = name.slice(3);
          if (parent()?.kind !== kind) fail(`@${name} has no matching open @${kind} at this level.`);
          if (conditionStack.length) fail(`Conditional block must end before @${name}.`);
          stack.pop(); headers = 0; helpBoxes = 0; break;
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
  for (const c of conditionStack) report(c.d, `@${c.kind} has no matching @end${c.kind}.`);
  for (const c of stack) report(c.d, `@${c.kind} has no matching @end${c.kind}.`);
  for (const g of containers) {
    if (g.kind === 'tabs' && !g.pages.size) report(g.d, '@tabs must contain at least one @tab page.');
    if (!g.parameters.some(p => !p.hidden)) report(g.d, `@${g.kind} must contain at least one visible parameter or standalone preview.`);
    if (g.header) {
      const controls = names.get(g.header);
      if (!controls) report(g.d, `@${g.kind} header parameter ${g.header} is not declared in the container.`);
      else if (controls.length !== 1) report(g.d, `@${g.kind} header parameter ${g.header} must be declared once.`);
      else if (controls[0].container !== g || controls[0].condition || controls[0].enableIf.length)
        report(g.d, `@${g.kind} header parameter ${g.header} must be unconditional and directly inside its own container.`);
      else if (g.kind === 'tab' && (controls[0].type !== 'toggle' || components(controls[0].valueType) !== 1))
        report(g.d, `@tab header parameter ${g.header} must be a scalar toggle.`);
    }
  }
  for (const c of conditions) {
    // Like Unity, conditions attached to no declarations have no dependencies.
    if (!c.hasRow) continue;
    const [name, channel] = c.name.split('.');
    const controls = names.get(name);
    const count = controls ? components(controls[0].valueType || nativeType(controls[0].type)) : 0;
    const index = channel ? Math.max('xyzw'.indexOf(channel),'rgba'.indexOf(channel)) : 0;
    if (!controls) report(c.d, `@${c.kind} refers to undeclared parameter ${c.name}.`);
    else if (!count || channel && index >= count || !channel && count !== 1) report(c.d, `@${c.kind} requires a scalar or an explicit valid vector component.`);
    else if (controls.some(p => p.condition)) report(c.d, `@${c.kind} must reference a parameter outside @if blocks.`);
  }
  for (const preview of previews)
    for (const parameter of preview.previewParameters)
      if (!names.has(parameter)) report(preview.d, `Preview ${preview.previewFunction} references undeclared @param ${parameter}.`, true);
  if (control) {
    const fields = names.get(control.name);
    if (!fields) report(control.d, `@control refers to undeclared parameter ${control.name}.`, true);
    else if (fields.length !== 1) report(control.d, `@control parameter ${control.name} must have one declaration to select an unambiguous field.`, true);
  }
  return diagnostics;
}

module.exports = { validate, parseParameter, parsePreset, directiveLines };
