// Test lõi Profile demo/test (hàm thuần) — chạy: node --test "tests-js/*.test.mjs"
// Lõi nằm TRONG demo-dashboard.html (block <script id="profile-core">); file mẫu demo-profiles.example.js phải luôn hợp lệ.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const html = readFileSync(new URL('../demo-dashboard.html', import.meta.url), 'utf8');
const block = html.match(/<script id="profile-core">([\s\S]*?)<\/script>/);
if (!block) throw new Error('Không tìm thấy <script id="profile-core"> trong demo-dashboard.html');
const win = {};
new Function('window', block[1])(win);
const P = win.ProfileCore;

const full = {
  name: 'Test local',
  server: { host: 'localhost', port: '8000' },
  account: { email: 'a@b.c', password: 'pw' },
  device: { id: 'SIM_Zone1', name: 'Zone 1' },
  mqtt: { host: 'broker.example', user: 'planttree-sim', password: 'mq' },
  notify: true,
  mcpUrl: 'http://localhost:8100/mcp',
};

test('profileToFields fills every page field from a full profile', () => {
  assert.deepEqual(P.profileToFields(full), {
    'conn-host': 'localhost', 'conn-port': '8000', 'conn-email': 'a@b.c', 'conn-password': 'pw',
    'conn-device': 'SIM_Zone1', 'sim-id': 'SIM_Zone1', 'sim-name': 'Zone 1',
    'sim-mqtt-host': 'broker.example', 'sim-mqtt-user': 'planttree-sim', 'sim-mqtt-pass': 'mq',
    'mcp-url': 'http://localhost:8100/mcp',
  });
});

test('profileToFields skips missing parts (keeps what is already on the page)', () => {
  const f = P.profileToFields({ name: 'Mac', server: { host: 'https://api.example' } });
  assert.deepEqual(f, { 'conn-host': 'https://api.example' });
});

test('validateProfiles reports missing name/host, bad SIM id and duplicate names', () => {
  assert.deepEqual(P.validateProfiles([full]), []);
  const errs = P.validateProfiles([
    { server: { host: 'x' } },
    { name: 'A' },
    { name: 'B', server: { host: 'x' }, device: { id: 'ESP32S3_Zone1' } },
    { name: 'B', server: { host: 'y' } },
  ]);
  assert.equal(errs.length, 4);
  assert.match(errs.join('\n'), /thiếu name/);
  assert.match(errs.join('\n'), /thiếu server\.host/);
  assert.match(errs.join('\n'), /SIM_/);
  assert.match(errs.join('\n'), /trùng/);
  assert.deepEqual(P.validateProfiles('nope'), ['PLANTTREE_PROFILES phải là một mảng']);
});

test('demo-profiles.example.js stays valid and has a test + a real-demo profile', () => {
  const code = readFileSync(new URL('../demo-profiles.example.js', import.meta.url), 'utf8');
  const w = {};
  new Function('window', code)(w);
  const list = w.PLANTTREE_PROFILES;
  assert.ok(Array.isArray(list) && list.length >= 2);
  assert.deepEqual(P.validateProfiles(list), []);
  for (const p of list) {
    const f = P.profileToFields(p);
    for (const id of ['conn-host', 'conn-email', 'conn-password', 'sim-id', 'sim-mqtt-host', 'sim-mqtt-user', 'sim-mqtt-pass'])
      assert.ok(f[id], `${p.name}: thiếu ${id}`);
  }
});
