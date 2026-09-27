// Diagnostic only: reads published JSON; never reconstructs authoring descriptors.
// Usage: node tools/inspect-runtime020-sdk.mjs <extracted-sdk-directory>
import { readFileSync, statSync } from 'node:fs';
import { resolve } from 'node:path';
import { createHash } from 'node:crypto';

const directory = process.argv[2];
if (!directory) throw new Error('Supply the extracted published SDK directory.');
const hashes = {};
function read(name) {
    const path = resolve(directory, name);
    if (statSync(path).size > 8 * 1024 * 1024) throw new Error(`Oversized artifact: ${name}`);
    const bytes = readFileSync(path);
    hashes[name] = createHash('sha256').update(bytes).digest('hex');
    return JSON.parse(bytes);
}
const authoring = read('SupportWeaponAuthoringCapabilities.json');
const inspection = read('SupportWeaponCapabilities.json');
const canonical = authoring.contract === 'hd2runtime.support_weapon.guarded_authoring.v2' && authoring.schemaVersion === 2 && authoring.hd2RuntimeVersion === '0.20.1';
if ((!canonical && (authoring.contract !== 'hd2runtime.support_weapon.guarded_authoring.v1' ||
    authoring.schemaVersion !== 1 || authoring.hd2RuntimeVersion !== '0.20.0')) ||
    inspection.hd2RuntimeVersion !== authoring.hd2RuntimeVersion)
    throw new Error('This audit targets the published 0.20.0 and 0.20.1 support contracts only.');

const weapons = authoring.weapons;
if (!Array.isArray(weapons) || new Set(weapons.map(w => w.name)).size !== weapons.length)
    throw new Error('Malformed or duplicate support catalog entries.');
const countIds = w => Object.values(w.writableFieldsByDomain).reduce((n, fields) => n + fields.length, 0);
const reportedInstances = weapons.reduce((n, w) => n + w.writableFieldCount, 0);
if (weapons.length !== authoring.summary.catalogWeapons ||
    weapons.filter(w => w.writable).length !== authoring.summary.writableSupportWeapons ||
    reportedInstances !== authoring.summary.writableFieldInstances)
    throw new Error('Catalog summary does not match weapon totals.');

if (canonical) {
    const fields = new Map(authoring.fieldInstances.map(f => [f.instanceKey, f]));
    if (fields.size !== reportedInstances || fields.size !== authoring.fieldInstances.length || authoring.instanceAudit.missingInstances !== 0)
        throw new Error('Canonical instances missing or duplicated.');
    for (const w of weapons) if (w.fieldInstanceKeys.length !== w.writableFieldCount || w.fieldInstanceKeys.some(k => fields.get(k)?.supportWeapon !== w.name))
        throw new Error('Canonical weapon/instance join mismatch.');
    for (const f of fields.values()) if (!authoring.backingObjects.some(o => o.objectKey === f.backing.objectKey && o.fieldInstanceKeys.includes(f.instanceKey)) ||
        !authoring.operationGroups.some(o => o.operationGroupingKey === f.operation.transactionGroupingKey && o.fieldInstanceKeys.includes(f.instanceKey)))
        throw new Error('Canonical object/operation join mismatch.');
}
console.log(JSON.stringify({
    canonicalInstances: canonical ? authoring.fieldInstances.length : 0,
    canonicalMissingInstances: authoring.instanceAudit?.missingInstances,
    backingObjects: authoring.backingObjects?.length,
    operationGroups: authoring.operationGroups?.length,
    sharedScopes: canonical ? new Set(authoring.fieldInstances.filter(f => f.sharedScope.shared).map(f => f.sharedScope.scopeKey)).size : 0,
    version: authoring.hd2RuntimeVersion,
    contract: authoring.contract,
    weapons: weapons.length,
    writableWeapons: weapons.filter(w => w.writable).length,
    reportedInstances,
    publishedWeaponFieldNames: weapons.reduce((n, w) => n + countIds(w), 0),
    legacyFlattenedMultiplicityLoss: weapons.filter(w => w.writableFieldCount !== countIds(w)).map(w => ({
        weapon: w.name, reportedInstances: w.writableFieldCount, publishedFieldNames: countIds(w)
    })),
    publishedWeaponProperties: [...new Set(weapons.flatMap(Object.keys))].sort(),
    fieldsByDomain: authoring.summary.fieldInstancesByDomain,
    scopeCategories: [...new Set(weapons.flatMap(w => w.sharedScopes))].sort(),
    blocked: weapons.filter(w => !w.writable).map(w => ({ name: w.name, reasons: w.blockedFields })),
    inspectionContract: inspection.contract,
    inspectionAuthoringReady: inspection.summary.guardedAuthoringReady,
    hashes,
    conclusion: canonical ? 'Canonical instance joins are complete; legacy flattened counts are not used for authoring.' : 'Summary metadata is not an instance-level authoring contract. See docs/runtime020-sdk-blocker.md.'
}, null, 2));
