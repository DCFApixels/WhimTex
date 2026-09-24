const vscode = require('vscode');

const parameterTypes = new Set([
  'float', 'bool', 'float2', 'float3', 'float4', 'normal', 'point', 'color',
  'texture2D', 'transform2D', 'gradient', 'curve', 'enum'
]);
const metadataDirectives = new Set([
  'param', 'if', 'endif', 'group', 'endgroup', 'header', 'helpbox', 'formerlyserializedas'
]);

function parseParameter(body) {
  let text = body.trim();
  text = text.replace(/^hidden\s+/, '');
  text = text.replace(/^label\((?:[^()]|\([^()]*\))*\)\s*/, '');
  text = text.replace(/^hidden\s+/, '');
  const enumMatch = text.match(/^enum\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s*=\s*[^{}]+?)?\s*\{[^{}]+\}\s*$/);
  if (enumMatch) return { type: 'enum', name: enumMatch[1] };
  const match = text.match(/^(float|bool|float2|float3|float4|normal|point|color|texture2D|transform2D|gradient|curve|enum)\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s*=\s*([^\[;]+?))?(?:\s*\[[^\]]*\])?\s*$/);
  if (!match) return null;
  return { type: match[1], name: match[2] };
}

function findLineComment(line, state) {
  let offset = 0;
  while (offset < line.length) {
    if (state.inBlockComment) {
      const close = line.indexOf('*/', offset);
      if (close < 0) return null;
      state.inBlockComment = false;
      offset = close + 2;
      continue;
    }
    const lineStart = line.indexOf('//', offset);
    const blockStart = line.indexOf('/*', offset);
    if (lineStart >= 0 && (blockStart < 0 || lineStart < blockStart))
      return /^\s*$/.test(line.slice(0, lineStart)) ? line.slice(lineStart) : null;
    if (blockStart < 0) return null;
    state.inBlockComment = true;
    offset = blockStart + 2;
  }
  return null;
}

function validate(document, diagnostics) {
  const names = new Map();
  const conditions = [];
  const conditionReferences = [];
  const groups = [];
  const commentState = { inBlockComment: false };
  const lines = document.getText().split(/\r?\n/);

  for (let index = 0; index < lines.length; index++) {
    const line = lines[index];
    const activeComment = findLineComment(line, commentState);
    const directive = activeComment && activeComment.match(/^\/\/\s*@([A-Za-z][A-Za-z0-9]*)(.*)$/);
    if (!directive) continue;
    const name = directive[1];
    const body = directive[2].trim();
    const range = new vscode.Range(index, 0, index, line.length);

    if (!metadataDirectives.has(name)) {
      diagnostics.push(new vscode.Diagnostic(range, `Unknown WhimTex directive @${directive[1]}.`, vscode.DiagnosticSeverity.Warning));
      continue;
    }
    if (name === 'param') {
      const parameter = parseParameter(body);
      if (!parameter) {
        diagnostics.push(new vscode.Diagnostic(range, 'Expected a supported // @param declaration.', vscode.DiagnosticSeverity.Error));
        continue;
      }
      if (!parameterTypes.has(parameter.type)) {
        diagnostics.push(new vscode.Diagnostic(range, `Unsupported parameter type: ${parameter.type}.`, vscode.DiagnosticSeverity.Error));
        continue;
      }
      if (names.has(parameter.name))
        diagnostics.push(new vscode.Diagnostic(range, `Parameter ${parameter.name} is declared more than once.`, vscode.DiagnosticSeverity.Error));
      else names.set(parameter.name, { type: parameter.type, line: index, conditional: conditions.length > 0 });
    } else if (name === 'if') {
      const match = body.match(/^([A-Za-z_][A-Za-z0-9_]*)\s*(==|!=)\s*(-?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)$/);
      if (!match) {
        diagnostics.push(new vscode.Diagnostic(range, 'Expected // @if _Parameter == number or // @if _Parameter != number.', vscode.DiagnosticSeverity.Error));
      } else if (conditions.length) {
        diagnostics.push(new vscode.Diagnostic(range, 'Nested @if blocks are not supported.', vscode.DiagnosticSeverity.Error));
      } else {
        const condition = { name: match[1], line: index };
        conditions.push(condition);
        conditionReferences.push(condition);
      }
    } else if (name === 'endif') {
      if (body.length) diagnostics.push(new vscode.Diagnostic(range, '// @endif takes no arguments.', vscode.DiagnosticSeverity.Error));
      if (!conditions.length) diagnostics.push(new vscode.Diagnostic(range, '@endif has no matching @if.', vscode.DiagnosticSeverity.Error));
      else conditions.pop();
    } else if (name === 'group') {
      if (groups.length) diagnostics.push(new vscode.Diagnostic(range, 'Nested @group blocks are not supported.', vscode.DiagnosticSeverity.Error));
      if (conditions.length) diagnostics.push(new vscode.Diagnostic(range, '@group must be outside conditional blocks.', vscode.DiagnosticSeverity.Error));
      if (body && !/^\(.+\)$/.test(body)) diagnostics.push(new vscode.Diagnostic(range, 'Expected // @group, // @group(Title), or // @group(Title; _Parameter).', vscode.DiagnosticSeverity.Error));
      else groups.push({ line: index, parameterCount: 0 });
    } else if (name === 'endgroup') {
      if (body.length) diagnostics.push(new vscode.Diagnostic(range, '// @endgroup takes no arguments.', vscode.DiagnosticSeverity.Error));
      if (!groups.length) diagnostics.push(new vscode.Diagnostic(range, '@endgroup has no matching @group.', vscode.DiagnosticSeverity.Error));
      else groups.pop();
    } else if (name === 'formerlyserializedas' && !/^\([A-Za-z_][A-Za-z0-9_]*\)$/.test(body)) {
      diagnostics.push(new vscode.Diagnostic(range, 'Expected // @formerlyserializedas(_OldName).', vscode.DiagnosticSeverity.Error));
    } else if ((name === 'header' || name === 'helpbox') && !/^\(.+\)$/.test(body)) {
      diagnostics.push(new vscode.Diagnostic(range, `Expected // @${name}(text).`, vscode.DiagnosticSeverity.Error));
    }
  }

  for (const condition of conditions)
    diagnostics.push(new vscode.Diagnostic(new vscode.Range(condition.line, 0, condition.line, lines[condition.line].length), '@if has no matching @endif.', vscode.DiagnosticSeverity.Error));
  for (const group of groups)
    diagnostics.push(new vscode.Diagnostic(new vscode.Range(group.line, 0, group.line, lines[group.line].length), '@group has no matching @endgroup.', vscode.DiagnosticSeverity.Error));

  for (const condition of conditionReferences) {
    const parameter = names.get(condition.name);
    const line = lines[condition.line];
    const range = new vscode.Range(condition.line, 0, condition.line, line.length);
    if (!parameter) diagnostics.push(new vscode.Diagnostic(range, `@if refers to undeclared parameter ${condition.name}.`, vscode.DiagnosticSeverity.Error));
    else if (!['float', 'bool', 'enum'].includes(parameter.type))
      diagnostics.push(new vscode.Diagnostic(range, '@if can reference only float, bool, or enum parameters.', vscode.DiagnosticSeverity.Error));
    else if (parameter.conditional)
      diagnostics.push(new vscode.Diagnostic(range, '@if must reference an unconditional parameter.', vscode.DiagnosticSeverity.Error));
  }
}

function isWhimTexCodeDocument(document) {
  if (document.languageId !== 'hlsl' || document.uri.scheme !== 'file') return false;
  const normalizedPath = document.uri.fsPath.replace(/\\/g, '/');
  return /\/Library\/WhimTex\/ExternalCode\/[^/]+\.hlsl$/i.test(normalizedPath);
}

function activate(context) {
  const collection = vscode.languages.createDiagnosticCollection('whimtex-fx');
  const update = document => {
    if (document.languageId !== 'hlsl') return;
    const diagnostics = [];
    validate(document, diagnostics);
    collection.set(document.uri, diagnostics);
  };
  context.subscriptions.push(collection);
  context.subscriptions.push(vscode.workspace.onDidOpenTextDocument(update));
  context.subscriptions.push(vscode.workspace.onDidChangeTextDocument(event => update(event.document)));
  context.subscriptions.push(vscode.workspace.onDidSaveTextDocument(update));
  context.subscriptions.push(vscode.workspace.onDidSaveTextDocument(document => {
    if (!isWhimTexCodeDocument(document)) return;
    const requestUri = vscode.Uri.file(document.uri.fsPath + '.apply');
    vscode.workspace.fs.writeFile(requestUri, Buffer.from(Date.now().toString()))
      .then(() => {}, error => vscode.window.showErrorMessage(`WhimTex could not request Apply: ${error.message}`));
  }));
  for (const document of vscode.workspace.textDocuments) update(document);
  context.subscriptions.push(vscode.languages.registerCompletionItemProvider('hlsl', {
    provideCompletionItems(document, position) {
      const linePrefix = document.lineAt(position.line).text.slice(0, position.character);
      const directivePrefix = linePrefix.match(/@[A-Za-z]*$/);
      const replacementRange = directivePrefix
        ? new vscode.Range(position.line, position.character - directivePrefix[0].length, position.line, position.character)
        : undefined;
      return ['@param', '@if', '@endif', '@group()', '@endgroup', '@header()', '@helpbox()', '@formerlyserializedas()']
        .map(label => {
          const item = new vscode.CompletionItem(label, vscode.CompletionItemKind.Keyword);
          item.insertText = label + (label === '@param' || label === '@if' ? ' ' : '');
          item.range = replacementRange;
          return item;
        });
    }
  }, '@'));
}

function deactivate() {}
module.exports = { activate, deactivate, validateForTests: validate, isWhimTexCodeDocumentForTests: isWhimTexCodeDocument };
