import {cpSync,readFileSync,existsSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {join} from 'node:path';
const root=fileURLToPath(new URL('../../docs/',import.meta.url));
const check=process.argv.includes('--check');
const shared=fileURLToPath(new URL('../Web/release-selection.js',import.meta.url));
for(const app of ['CPClipboard','CPFileLauncher','CPSystemMonitor']){const target=join(root,app,'assets/release-selection.js');if(check){if(!existsSync(target)||!readFileSync(target).equals(readFileSync(shared)))throw new Error('Shared release selector is out of date: '+target);}else cpSync(shared,target);}
