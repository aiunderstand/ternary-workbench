// VS Code shim for the REBEL-6 debug adapter: the adapter is an external DAP
// executable (TernaryWorkbench.DebugAdapter, DAP over stdio); this extension only
// tells VS Code how to start it and fills launch-config defaults from settings.
const vscode = require('vscode');

function activate(context) {
  context.subscriptions.push(
    vscode.debug.registerDebugConfigurationProvider('rebel6', {
      resolveDebugConfiguration(_folder, config) {
        // "F5 on a .tas file with no launch.json" path
        if (!config.type && !config.request && !config.name) {
          const editor = vscode.window.activeTextEditor;
          if (editor && editor.document.fileName.endsWith('.tas')) {
            config.type = 'rebel6';
            config.request = 'launch';
            config.name = 'Debug REBEL-6 program';
            config.program = editor.document.fileName;
            config.stopOnEntry = true;
          }
        }
        if (config.request === 'launch' && !config.simulator) {
          config.simulator = vscode.workspace.getConfiguration('rebel6').get('simulatorPath');
        }
        return config;
      },
    }),
    vscode.debug.registerDebugAdapterDescriptorFactory('rebel6', {
      createDebugAdapterDescriptor() {
        const configured = vscode.workspace.getConfiguration('rebel6').get('adapterPath');
        const adapter = configured && configured.length > 0 ? configured : 'TernaryWorkbench.DebugAdapter';
        if (adapter.endsWith('.dll')) {
          return new vscode.DebugAdapterExecutable('dotnet', [adapter]);
        }
        return new vscode.DebugAdapterExecutable(adapter, []);
      },
    })
  );
}

function deactivate() {}

module.exports = { activate, deactivate };
