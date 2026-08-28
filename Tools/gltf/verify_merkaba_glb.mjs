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
if (root.listScenes().length !== 1 || root.listMeshes().length !== 1 ||
    root.listMaterials().length !== 1 || root.listTextures().length !== 0) {
  throw new Error('Independent NodeIO graph does not match one untextured PBR mesh.');
}
const material = root.listMaterials()[0];
if (material.getMetallicFactor() !== 0 ||
    Math.abs(material.getRoughnessFactor() - 0.85) > 1e-6 ||
    material.getBaseColorTexture() || material.getNormalTexture()) {
  throw new Error('Material is not the required matte, metallic-zero vertex-color PBR.');
}
const primitive = root.listMeshes()[0].listPrimitives()[0];
if (!primitive || !primitive.getAttribute('POSITION') ||
    !primitive.getAttribute('NORMAL') || !primitive.getAttribute('COLOR_0') ||
    !primitive.getIndices() || primitive.getAttribute('TEXCOORD_0')) {
  throw new Error('Expected indexed POSITION/NORMAL/COLOR_0 geometry is missing.');
}

const report = {
  schemaVersion: 1,
  fixture: path.resolve(fixturePath),
  byteLength: bytes.length,
  validatorErrors: validation.issues.numErrors,
  validatorWarnings: validation.issues.numWarnings,
  vertices: primitive.getAttribute('POSITION').getCount(),
  indices: primitive.getIndices().getCount(),
  attributes: primitive.listSemantics(),
  metallicFactor: material.getMetallicFactor(),
  roughnessFactor: material.getRoughnessFactor(),
};
const reportPath = fixturePath + '.validation.json';
await fs.writeFile(reportPath, `${JSON.stringify(report, null, 2)}\n`);
console.log(`GLB interoperability passed: ${reportPath}`);
