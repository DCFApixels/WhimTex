const vscode = require('vscode');

const metadata = require('./metadata');
// Block directives come from contributed snippets, including native Tab completion.
const completionDirectives = [
  '@param', '@endif', '@endgroup', '@header()', '@helpbox()', '@formerlyserializedas()', '@whimtex-effect'
];

function validate(document, diagnostics) {
  for (const item of metadata.validate(document.getText())) {
    const range = new vscode.Range(item.line, item.start, item.line, item.end);
    diagnostics.push(new vscode.Diagnostic(range, item.message,
      item.warning ? vscode.DiagnosticSeverity.Warning : vscode.DiagnosticSeverity.Error));
  }
}

function isWhimTexCodeDocument(document) {
  if (document.languageId !== 'hlsl' || document.uri.scheme !== 'file') return false;
  const normalizedPath = document.uri.fsPath.replace(/\\/g, '/');
  return /\/Library\/WhimTex\/ExternalCode\/[^/]+\.hlsl$/i.test(normalizedPath);
}

function requestApplyAfterSave(document) {
  if (!isWhimTexCodeDocument(document)) return;
  const requestUri = vscode.Uri.file(document.uri.fsPath + '.apply');
  vscode.workspace.fs.writeFile(requestUri, Buffer.from(Date.now().toString()))
    .then(() => {}, error => vscode.window.showErrorMessage(`WhimTex could not request Apply: ${error.message}`));
}

function provideCompletionItems(document, position) {
  const linePrefix = document.lineAt(position.line).text.slice(0, position.character);
  const directivePrefix = linePrefix.match(/@[A-Za-z]*$/);
  const replacementRange = directivePrefix
    ? new vscode.Range(position.line, position.character - directivePrefix[0].length, position.line, position.character)
    : undefined;
  return completionDirectives.map(label => {
    const item = new vscode.CompletionItem(label, vscode.CompletionItemKind.Keyword);
    item.insertText = label + (label === '@param' ? ' ' : '');
    item.range = replacementRange;
    return item;
  });
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
  context.subscriptions.push(vscode.workspace.onDidSaveTextDocument(requestApplyAfterSave));
  for (const document of vscode.workspace.textDocuments) update(document);
  context.subscriptions.push(vscode.languages.registerCompletionItemProvider('hlsl', { provideCompletionItems }, '@'));
}

function deactivate() {}
module.exports = { activate, deactivate, validateForTests: validate, isWhimTexCodeDocumentForTests: isWhimTexCodeDocument };
