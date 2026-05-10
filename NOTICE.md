# Third-Party Notices

This project includes or depends upon the following third-party software components. Each component is subject to its own license terms as indicated below.

## Vendored Source

The following third-party sources are vendored in-tree under
`backend/src/AssetExtractor/UnityReader/Vendor/`:

- [AssetStudioMod](https://github.com/aelurum/AssetStudio) under `Vendor/AssetStudio/`
- [AssetRipper.TextureDecoder](https://github.com/AssetRipper/TextureDecoder) under `Vendor/AssetRipperTextureDecoder/`

Both are distributed by their authors under the MIT License. Their copyrights
and the MIT permission notice apply to the vendored portions and are reproduced
below verbatim:

```
Copyright (c) 2018 Perfare
Copyright (c) aelurum (AssetStudioMod fork)
Copyright (c) 2022 ds5678 (AssetRipper.TextureDecoder)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## NuGet Dependencies (.NET Packages)

### Microsoft Packages

- **Microsoft.AspNetCore.OpenApi** - MIT License
- **Microsoft.Extensions.FileProviders.Embedded** - MIT License
- **Microsoft.Extensions.Logging** - MIT License
- **Microsoft.Extensions.Logging.Abstractions** - MIT License
- **Microsoft.Extensions.Logging.Console** - MIT License

### Graphics and Image Processing

- **SharpGLTF.Toolkit** - MIT License - <https://github.com/vpenades/SharpGLTF>
- **SixLabors.ImageSharp** - Apache-2.0 / Six Labors Split License - <https://github.com/SixLabors/ImageSharp>
- **BCnEncoder.Net** - MIT License - <https://github.com/Nominom/BCnEncoder.NET>

### Compression

- **K4os.Compression.LZ4** - MIT License - <https://github.com/MiloszKrajewski/K4os.Compression.LZ4>

### System Utilities

- **System.IO.Hashing** - MIT License (part of .NET runtime libraries)
- **NotificationIcon.NET** - MIT License - <https://github.com/Bip901/NotificationIcon.NET>

## NPM Dependencies (Frontend JavaScript Packages)

### Core Framework

- **React** - MIT License - <https://github.com/facebook/react>
- **React-DOM** - MIT License - <https://github.com/facebook/react>
- **React Router DOM** - MIT License - <https://github.com/remix-run/react-router>

### 3D Graphics

- **Three.js** - MIT License - <https://github.com/mrdoob/three.js>
- **stats.js** - MIT License - <https://github.com/mrdoob/stats.js>

### State Management and Data Fetching

- **Zustand** - MIT License - <https://github.com/pmndrs/zustand>
- **@tanstack/react-query** - MIT License - <https://github.com/TanStack/query>

### Networking

- **Axios** - MIT License - <https://github.com/axios/axios>
- **@microsoft/signalr** - MIT License - <https://github.com/dotnet/aspnetcore>

### UI Components and Utilities

- **Tailwind CSS** - MIT License - <https://github.com/tailwindlabs/tailwindcss>
- **lucide-react** - ISC License - <https://github.com/lucide-icons/lucide>
- **class-variance-authority** - Apache-2.0 License - <https://github.com/joe-bell/cva>
- **clsx** - MIT License - <https://github.com/lukeed/clsx>
- **tailwind-merge** - MIT License - <https://github.com/dcastil/tailwind-merge>
- **lil-gui** - MIT License - <https://github.com/georgealways/lil-gui>
- **vanilla-picker** - ISC License - <https://github.com/Sphinxxxx/vanilla-picker>
- **dompurify** - Apache-2.0 License - <https://github.com/cure53/DOMPurify>

### Development Dependencies

- **TypeScript** - Apache-2.0 License - <https://github.com/microsoft/TypeScript>
- **Vite** - MIT License - <https://github.com/vitejs/vite>
- **ESLint** - MIT License - <https://github.com/eslint/eslint>

## License Compliance Notes

OldenEraExplorer is MIT-licensed. All listed dependencies and vendored sources are under permissive licenses (MIT, Apache-2.0, ISC, Six Labors Split). There are no copyleft components in the source tree.

## Full License Texts

For complete license texts, please refer to:

- MIT: <https://opensource.org/licenses/MIT>
- Apache-2.0: <https://www.apache.org/licenses/LICENSE-2.0>
- ISC: <https://opensource.org/licenses/ISC>
- Six Labors Split: <https://github.com/SixLabors/ImageSharp/blob/main/LICENSE>

For specific package licenses, consult the package source repositories listed above.
