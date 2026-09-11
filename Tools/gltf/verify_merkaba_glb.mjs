import fs from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import validator from 'gltf-validator';
import { NodeIO } from '@gltf-transform/core';

const fixturePath = process.argv[2];
if (!fixturePath) throw new Error('Merkaba GLB fixture path is required.');

const bytes = await fs.readFile(fixturePath);
const validation = await validator.validateBytes(new Uint8Array(bytes), {
  uri: path.basename(fixturePath),
  maxIssues: 1000,
  externalResourceFunction: async () => {
    throw new Error('Merkaba GLB unexpectedly referenced an external resource.');
  },
});
if (validation.issues.numErrors !== 0) {
  throw new Error(`Khronos validator reported ${validation.issues.numErrors} error(s): ` +
    JSON.stringify(validation.issues.messages));
}

const document = await new NodeIO().read(fixturePath);
const root = document.getRoot();
if (root.listScenes().length !== 1 || root.listMeshes().length !== 1) {
  throw new Error('Independent NodeIO graph does not match the Flower presentation mesh.');
}
// Inspect extension declarations independently: NodeIO need not implement
// the optional native-record extension to open the portable preview.
const json = JSON.parse(bytes.subarray(20, 20 + bytes.readUInt32LE(12)).toString('utf8'));
if (!json.extensionsUsed?.includes('KHR_materials_unlit') ||
    json.extensionsRequired?.includes('M8_flower_thread'))
  throw new Error('Captured-radiance preview/native-record extension policy is invalid.');

const primitives = root.listMeshes()[0].listPrimitives();
if (primitives.length === 0) throw new Error('Flower fixture emitted no primitives.');
for (const [index, primitive] of primitives.entries()) {
  const description = json.meshes[0].primitives[index];
  const material = json.materials[description.material];
  const pbr = material?.pbrMetallicRoughness;
  if (!primitive.getAttribute('POSITION') || !primitive.getAttribute('COLOR_0') ||
      !primitive.getIndices() || primitive.getIndices().getCount() % 3 !== 0 ||
      primitive.getAttribute('NORMAL') || primitive.getAttribute('TANGENT'))
    throw new Error('Expected shared POSITION/COLOR_0 and flat-L2 indexed geometry is missing.');
  if (!material?.extensions?.KHR_materials_unlit || pbr?.metallicFactor !== 0 ||
      pbr.roughnessFactor !== 1)
    throw new Error('Flower preview must preserve captured radiance, not invent PBR albedo.');
  const textured = pbr.baseColorTexture !== undefined;
  if (textured !== Boolean(primitive.getAttribute('TEXCOORD_0')) ||
      textured !== Boolean(material.normalTexture))
    throw new Error('Flower texture primitive must carry paired RGB/V atlases and their UV chart.');
  if (textured && (material.extras?.m8NormalTexture !== 'derived-V-lit-fallback' ||
      material.extras?.normalFrame !== 'flat-L2-UV'))
    throw new Error('V normal atlas must declare its flat-L2 frame and unlit fallback limitation.');
  if (description.extras?.m8Provenance === 'DIRT' &&
      (textured || description.extras.capturedRadiance !== false ||
       description.extras.inferredSupport !== true))
    throw new Error('DIRT must remain untextured, explicitly inferred support.');
}

const report = {
  schemaVersion: 2,
  fixture: path.resolve(fixturePath),
  byteLength: bytes.length,
  validatorErrors: validation.issues.numErrors,
  validatorWarnings: validation.issues.numWarnings,
  primitives: primitives.map(primitive => ({
    vertices: primitive.getAttribute('POSITION').getCount(),
    indices: primitive.getIndices().getCount(),
    attributes: primitive.listSemantics(),
  })),
  textures: root.listTextures().length,
  normalTextureUse: json.extras?.normalTextureUse,
};
const reportPath = fixturePath + '.validation.json';
await fs.writeFile(reportPath, `${JSON.stringify(report, null, 2)}\n`);
console.log(`GLB interoperability passed: ${reportPath}`);
