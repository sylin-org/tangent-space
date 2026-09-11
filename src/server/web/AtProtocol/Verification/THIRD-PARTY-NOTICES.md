# Source adaptation and dependency notices

The protocol composition in `SpaceCarVerifier.cs` follows the algorithms and
format in Bluesky Social's AT Protocol repository, commit
`c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae`, notably
`packages/space/src/repo-commit.ts`, `lthash.ts`, `sync/consumer.ts`, and
`packages/syntax/src/{nsid,recordkey}.ts`. This adaptation uses its MIT license
option. The upstream repository provides this license text:

> MIT License
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.

Referenced NuGet packages declare MIT licenses in their published metadata:
CarpaNet `1.1.0-alpha.5`, BouncyCastle.Cryptography `2.7.0`, and
System.Formats.Cbor `10.0.5`. Their respective package license notices remain
applicable when distributing binaries. Package references and content hashes
are retained in the project and lockfile.
