import fs from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import validator from 'gltf-validator';
import { NodeIO } from '@gltf-transform/core';

const fixtureDirectory = process.argv[2];
if (!fixtureDirectory) throw new Error('Fixture directory argument is required.');
const fixtureNames = ['chunk.glb', 'chunk-second.glb', 'world.glb'];
const io = new NodeIO();
const reports = [];

for (const fixtureName of fixtureNames) {
  const fixturePath = path.join(fixtureDirectory, fixtureName);
  const bytes = await fs.readFile(fixturePath);
  const validation = await validator.validateBytes(new Uint8Array(bytes), {
    uri: fixtureName,
    maxIssues: 1000,
    externalResourceFunction: async () => {
      throw new Error(`${fixtureName} unexpectedly referenced an external resource.`);
    },
  });
  if (validation.issues.numErrors !== 0) {
    throw new Error(`${fixtureName}: Khronos validator reported ` +
      `${validation.issues.numErrors} error(s): ${JSON.stringify(validation.issues.messages)}`);
  }

  const document = await io.read(fixturePath);
  const root = document.getRoot();
  const expectedMeshes = fixtureName === 'world.glb' ? 2 : 1;
  if (root.listScenes().length !== 1 || root.listMeshes().length !== expectedMeshes ||
      root.listMaterials().length !== expectedMeshes ||
      root.listTextures().length !== expectedMeshes * 2) {
    throw new Error(`${fixtureName}: independent NodeIO graph counts are wrong.`);
  }
  for (const material of root.listMaterials()) {
    if (material.getMetallicFactor() !== 0 ||
        Math.abs(material.getRoughnessFactor() - 0.8) > 1e-6 ||
        !material.getBaseColorTexture() || !material.getNormalTexture()) {
      throw new Error(`${fixtureName}: independent NodeIO rejected honest PBR bindings.`);
    }
  }
  for (const mesh of root.listMeshes()) {
    const primitive = mesh.listPrimitives()[0];
    if (!primitive || !primitive.getAttribute('POSITION') ||
        !primitive.getAttribute('NORMAL') || !primitive.getAttribute('TANGENT') ||
        !primitive.getAttribute('TEXCOORD_0') || !primitive.getIndices()) {
      throw new Error(`${fixtureName}: expected indexed PNTUV geometry is missing.`);
    }
  }
  if (fixtureName === 'world.glb') {
    const nodes = root.listNodes();
    if (nodes.length !== 2) throw new Error('world.glb: expected two chunk nodes.');
    const translated = nodes.find((node) => node.getName().startsWith('chunk-000001_'));
    const translation = translated?.getTranslation();
    if (!translation || Math.abs(translation[0] + 2) > 1e-5 ||
        Math.abs(translation[1] - 3) > 1e-5 || Math.abs(translation[2] - 4) > 1e-5) {
      throw new Error('world.glb: worldFromChunk handedness/translation is wrong.');
    }
  }
  reports.push({
    fixture: fixtureName,
    validatorErrors: validation.issues.numErrors,
    validatorWarnings: validation.issues.numWarnings,
    meshes: root.listMeshes().length,
    materials: root.listMaterials().length,
    textures: root.listTextures().length,
  });
}

const reportPath = path.join(fixtureDirectory, 'interoperability-report.json');
await fs.writeFile(reportPath, `${JSON.stringify({ schemaVersion: 1, reports }, null, 2)}\n`);
console.log(`GLB interoperability passed: ${reportPath}`);
