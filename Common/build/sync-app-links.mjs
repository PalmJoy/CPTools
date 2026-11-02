// Static links work without JavaScript, and stay identical across product pages.
import {readFileSync,writeFileSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
const docs=fileURLToPath(new URL('../../docs/',import.meta.url));
const check=process.argv.includes('--check');
const products=[
  {name:'CPFileLauncher',ja:'ファイルランチャー',en:'File launcher'},
  {name:'CPClipboard',ja:'定型文・コピー履歴',en:'Snippets & clipboard history'},
  {name:'CPSystemMonitor',ja:'システムモニター',en:'System monitor'}
];
const start='<!-- CPTOOLS-LINKS:START -->',end='<!-- CPTOOLS-LINKS:END -->';
for(const current of products)for(const ja of [false,true]){
  const up=ja?'../../':'../',locale=ja?'ja/':'';
  const cells=products.map(product=>{
    const active=product.name===current.name;
    return `<a class="suite-cell" href="${up}${product.name}/${locale}"${active?' aria-current="page"':''}><span class="cell-arrow" aria-hidden="true">↗</span>${active?`<span class="cell-current">${ja?'閲覧中':'Current page'}</span>`:''}<img src="${up}${product.name}/assets/icon.svg" alt="" width="68" height="68" loading="lazy"><strong>${product.name}</strong><small>${ja?product.ja:product.en}</small></a>`;
  }).join('\n');
  const section=`${start}
<section class="suite-links" id="cptools" aria-labelledby="cptools-title"><div class="wrap"><div class="suite-links-heading"><div><span class="num">CPTOOLS / APPLICATIONS</span><h2 id="cptools-title">${ja?'ほかのCPToolsもチェック':'Explore the CPTools collection'}</h2></div><a class="suite-home" href="${up}${locale}#apps">← ${ja?'アプリ一覧へ':'All apps'}</a></div><nav class="suite-links-grid" aria-label="${ja?'CPToolsのアプリ':'CPTools applications'}">
${cells}
</nav></div></section>
${end}`;
  const path=`${docs}${current.name}/${locale}index.html`;
  const before=readFileSync(path,'utf8');
  if(before.includes(start)!==before.includes(end)||(!before.includes(start)&&!before.includes('</main>')))throw new Error('Invalid application link insertion point: '+path);
  const after=before.includes(start)
    ?before.slice(0,before.indexOf(start))+section+before.slice(before.indexOf(end)+end.length)
    :before.replace('</main>',section+'\n</main>');
  if(before===after)continue;
  if(check)throw new Error('Shared application links are out of date: '+path);
  writeFileSync(path,after);
}
