#!/usr/bin/env node
const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');
const os = require('os');

const APP_NAME = 'OldenEraExplorer';
const VERSION = process.argv[2] || process.env.VERSION || '';
const PACKAGE_BASE = VERSION ? `${APP_NAME}-v${VERSION}` : APP_NAME;
const RIDS = ['win-x64', 'linux-x64'];

const ROOT = path.resolve(__dirname, '..');
process.chdir(ROOT);

const run = (cmd) => {
    console.log(`> ${cmd}`);
    execSync(cmd, { stdio: 'inherit', shell: true });
};

const rmrf = (dir) => {
    if (fs.existsSync(dir)) {
        fs.rmSync(dir, { recursive: true, force: true });
    }
};

console.log('=========================================');
console.log(`Building Olden Era Explorer Release${VERSION ? ` v${VERSION}` : ''}`);
console.log('=========================================\n');

// Step 1: Clean dist
console.log('Step 1: Cleaning dist directory...');
rmrf('dist');
fs.mkdirSync('dist', { recursive: true });

// Step 2: Build for all platforms
console.log('\nStep 2: Building for all platforms...');
for (const rid of RIDS) {
    console.log(`\n  Building for ${rid}...`);
    run(`dotnet publish backend/src/API/API.csproj -c Release -r ${rid} --self-contained true -o dist/${rid} -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false`);

    console.log(`  Cleaning up ${rid}...`);
    const distRid = path.join('dist', rid);
    fs.readdirSync(distRid).forEach(f => {
        const fp = path.join(distRid, f);
        if (f.endsWith('.pdb') || f.endsWith('.json') || f === 'web.config') {
            fs.unlinkSync(fp);
        }
    });
    rmrf(path.join(distRid, 'wwwroot'));
    rmrf(path.join(distRid, 'ExtractedAssets'));
    rmrf(path.join(distRid, 'CustomAssets'));

    console.log(`  ${rid} build complete!`);
}

// Step 3: Cleanup
console.log('\nStep 3: Cleaning up temporary build files...');
rmrf('backend/src/API/wwwroot');

// Step 4: Create zips
console.log('\nStep 4: Creating zip packages...');
for (const rid of RIDS) {
    const zipName = `${PACKAGE_BASE}-${rid}.zip`;
    const ridDir = path.join('dist', rid);
    const zipPath = path.join('dist', zipName);

    if (os.platform() === 'win32') {
        run(`powershell -Command "Compress-Archive -Path '${ridDir}/*' -DestinationPath '${zipPath}' -Force"`);
    } else {
        run(`cd dist/${rid} && zip -r ../${zipName} . && cd ../..`);
    }
    console.log(`  Created: dist/${zipName}`);
}

console.log('\n=========================================');
console.log('Release build complete!');
console.log('\nPackages:');
for (const rid of RIDS) {
    console.log(`  dist/${PACKAGE_BASE}-${rid}.zip`);
}
console.log('=========================================');
