// Test lõi thiết bị ảo (hàm thuần) — chạy: node --test tests-js/sim-core.test.mjs
// Lõi nằm TRONG demo-dashboard.html (block <script id="sim-core">) để dashboard vẫn là 1 file mang đi được;
// test nạp block đó trong CÙNG realm (new Function) để deepStrictEqual so prototype đúng.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const html = readFileSync(new URL('../demo-dashboard.html', import.meta.url), 'utf8');
const block = html.match(/<script id="sim-core">([\s\S]*?)<\/script>/);
if (!block) throw new Error('Không tìm thấy <script id="sim-core"> trong demo-dashboard.html');
const win = {};
new Function('window', block[1])(win);
const Sim = win.SimCore;

const T0 = 1_000_000;
const fresh = (mode = 'auto') => Sim.createState('SIM_Test', mode);
const noRand = () => 0.5; // dao động = 0 -> kết quả xác định

const TELEMETRY_FIELDS = [
  'device_id', 'temperature_c', 'humidity_percent', 'pressure_hpa', 'altitude_m', 'temperature_bmp_c',
  'light_lux', 'soil_percent', 'soil_dry_flag', 'battery_voltage_v', 'battery_current_ma', 'battery_power_mw',
  'battery_percent', 'light_on', 'light_pwm', 'pump_on', 'mode', 'low_batt', 'batt_full', 'batt_cut', 'water_ok',
];

test('telemetry has exactly the 21 contract fields', () => {
  const t = Sim.toTelemetry(fresh());
  assert.deepEqual(Object.keys(t).sort(), [...TELEMETRY_FIELDS].sort());
  assert.equal(t.device_id, 'SIM_Test');
  assert.equal(t.soil_percent, 45);
  assert.equal(t.mode, 'auto');
  assert.equal(t.battery_voltage_v, 8);
});

test('pump / light / light_pwm commands never change mode', () => {
  for (const mode of ['auto', 'manual']) {
    const s = fresh(mode);
    Sim.applyControl(s, { pump: true }, T0);
    Sim.applyControl(s, { light: true }, T0);
    Sim.applyControl(s, { light_pwm: 90 }, T0);
    assert.equal(s.mode, mode);
    assert.equal(s.pumpOn, true);
    assert.equal(s.lightOn, true);
    assert.equal(s.lightPwm, 90);
  }
});

test('manual off overrides a running pump while staying in auto', () => {
  const s = fresh('auto');
  Sim.applyControl(s, { pump: true, mode: 'auto' }, T0);
  const r = Sim.applyControl(s, { pump: false, mode: 'auto' }, T0 + 3000);
  assert.equal(r.changed, true);
  assert.equal(s.pumpOn, false);
  assert.equal(s.mode, 'auto');
});

test('only mode / auto keys change mode', () => {
  const s = fresh('auto');
  Sim.applyControl(s, { mode: 'manual' }, T0); assert.equal(s.mode, 'manual');
  Sim.applyControl(s, { auto: true }, T0); assert.equal(s.mode, 'auto');
  Sim.applyControl(s, { auto: false }, T0); assert.equal(s.mode, 'manual');
  Sim.applyControl(s, { mode: 'turbo' }, T0); assert.equal(s.mode, 'manual'); // giá trị lạ -> bỏ qua
});

test('pump switches itself off after pump_max_run_s (20s)', () => {
  const s = fresh();
  Sim.applyControl(s, { pump: true }, T0);
  Sim.step(s, 10, T0 + 10_000, noRand);
  assert.equal(s.pumpOn, true);
  Sim.step(s, 10, T0 + 20_000, noRand);
  assert.equal(s.pumpOn, false);
  assert.equal(Math.round(s.soil), 61); // 45 + 8 + 8
});

test('soil dries 1% per 10s and does not rise when the tank is empty', () => {
  const s = fresh();
  Sim.step(s, 10, T0, noRand);
  assert.equal(Math.round(s.soil), 44);
  Sim.applyScenario(s, 'water-empty');
  Sim.applyControl(s, { pump: true }, T0);
  Sim.step(s, 10, T0 + 10_000, noRand);
  assert.equal(Math.round(s.soil), 43);
  assert.equal(s.pumpOn, true); // bơm chạy không
});

test('dry-soil scenario sets soil 15 and soil_dry_flag', () => {
  const s = fresh();
  assert.equal(Sim.applyScenario(s, 'dry-soil'), true);
  const t = Sim.toTelemetry(s);
  assert.equal(t.soil_percent, 15);
  assert.equal(t.soil_dry_flag, true);
});

test('low battery raises low_batt; voltage follows percent (2S)', () => {
  const s = fresh();
  Sim.applyScenario(s, 'low-battery');
  const t = Sim.toTelemetry(s);
  assert.equal(t.low_batt, true);
  assert.equal(t.battery_percent, 15);
  assert.equal(t.battery_voltage_v, 6.7);
  assert.equal(t.batt_cut, false);
});

test('battery cut switches loads off in any mode and ignores turn-on; full charge recovers', () => {
  for (const mode of ['auto', 'manual']) {
    const s = fresh(mode);
    Sim.applyControl(s, { pump: true, light: true }, T0);
    Sim.applyScenario(s, 'battery-cut');
    assert.equal(s.battCut, true);
    assert.equal(s.pumpOn, false);
    assert.equal(s.lightOn, false);
    Sim.applyControl(s, { pump: true, light: true }, T0);
    assert.equal(s.pumpOn, false);
    assert.equal(s.lightOn, false);
    Sim.applyScenario(s, 'battery-full');
    assert.equal(s.battCut, false);
    assert.equal(s.battFull, true);
  }
});

test('auto light reacts only when lux crosses a threshold; manual override sticks', () => {
  const s = fresh('auto');
  Sim.applyScenario(s, 'night');
  assert.equal(s.lightOn, true);
  assert.equal(s.lightPwm, 180);
  Sim.applyControl(s, { light: false, mode: 'auto' }, T0);
  Sim.step(s, 10, T0 + 10_000, noRand);
  Sim.step(s, 10, T0 + 20_000, noRand);
  assert.equal(s.lightOn, false); // vẫn tôn trọng lệnh tay
  Sim.applyScenario(s, 'day');
  assert.equal(s.lightOn, false);
  Sim.applyScenario(s, 'night');
  assert.equal(s.lightOn, true); // lần vượt ngưỡng kế tiếp
});

test('auto light does nothing in manual mode', () => {
  const s = fresh('manual');
  Sim.applyScenario(s, 'night');
  assert.equal(s.lightOn, false);
});

test('config {} asks for an echo; partial config is clamped, merged, unknown keys ignored', () => {
  const s = fresh();
  const r1 = Sim.applyControl(s, { config: {} }, T0);
  assert.equal(r1.publishConfig, true);
  assert.deepEqual(Sim.configPayload(s).config, Sim.DEFAULT_CONFIG);
  Sim.applyControl(s, { config: { soil_on_pct: 150, light_auto_pwm: -4, batt_crit_v: 12, foo: 1, lux_on: 'x' } }, T0);
  assert.equal(s.config.soil_on_pct, 100);
  assert.equal(s.config.light_auto_pwm, 0);
  assert.equal(s.config.batt_crit_v, 9);
  assert.equal(s.config.lux_on, 120);
  assert.equal('foo' in s.config, false);
  assert.equal(Sim.DEFAULT_CONFIG.soil_on_pct, 30); // mặc định không bị sửa lây
});

test('message shows on TFT and clears after message_secs', () => {
  const s = fresh();
  Sim.applyControl(s, { message: 'Hello', message_secs: 15 }, T0);
  assert.equal(s.message, 'Hello');
  Sim.step(s, 10, T0 + 10_000, noRand);
  assert.equal(s.message, 'Hello');
  Sim.step(s, 10, T0 + 20_000, noRand);
  assert.equal(s.message, '');
});

test('garbage commands are ignored without changing state', () => {
  const s = fresh();
  for (const cmd of [null, 42, 'pump', [1], { pump: 'yes', light: 1, light_pwm: 'x' }]) {
    const r = Sim.applyControl(s, cmd, T0);
    assert.equal(r.changed, false);
    assert.equal(r.publishConfig, false);
  }
  assert.equal(s.pumpOn, false);
  assert.equal(s.lightOn, false);
  assert.equal(s.lightPwm, 180);
});

test('reset restores the initial state but keeps the server-owned mode', () => {
  const s = fresh('manual');
  Sim.applyScenario(s, 'dry-soil');
  Sim.applyControl(s, { pump: true }, T0);
  assert.equal(Sim.applyScenario(s, 'reset'), true);
  assert.equal(s.mode, 'manual');
  assert.equal(Math.round(s.soil), 45);
  assert.equal(s.pumpOn, false);
});

// Review #1: lệnh không đổi gì trong telemetry phải trả changed:false — nếu không, thiết bị ảo gửi telemetry
// ngay -> server (auto, đất khô, batt_cut) lại gửi {"pump":true} -> vòng lặp vô hạn ở tốc độ mạng.
test('no-op commands report changed:false (prevents a command/telemetry storm)', () => {
  const cut = fresh('auto');
  Sim.applyScenario(cut, 'battery-cut');
  assert.equal(Sim.applyControl(cut, { pump: true, mode: 'auto' }, T0).changed, false); // batt_cut bỏ qua bơm

  const on = fresh('auto');
  Sim.applyControl(on, { pump: true }, T0);
  assert.equal(Sim.applyControl(on, { pump: true, mode: 'auto' }, T0 + 1000).changed, false); // đã bật sẵn

  assert.equal(Sim.applyControl(fresh('auto'), { mode: 'auto' }, T0).changed, false); // cùng mode
  assert.equal(Sim.applyControl(fresh(), { light_pwm: 180 }, T0).changed, false);      // cùng PWM
  assert.equal(Sim.applyControl(fresh(), { pump: true }, T0).changed, true);           // đổi thật
});

// Review #2: mqtt.js 5 mặc định KHÔNG thử lại khi broker từ chối CONNECT (sai user/pass).
test('MQTT options keep retrying after a refused CONNACK', () => {
  const o = Sim.mqttOptions('SIM_Zone1', 'u', 'p', () => 0.5);
  assert.equal(o.reconnectOnConnackError, true);
  assert.equal(o.reconnectPeriod, 5000);
  assert.equal(o.username, 'u');
  assert.equal(o.password, 'p');
  assert.match(o.clientId, /^planttree-sim-SIM_Zone1-/);
});

test('MQTT errors get a readable reason for the badge', () => {
  assert.match(Sim.mqttErrorReason({ code: 5, message: 'Connection refused: Not authorized' }), /user\/password/);
  assert.match(Sim.mqttErrorReason({ code: 4, message: 'Bad username or password' }), /user\/password/);
  assert.match(Sim.mqttErrorReason({ code: 135, message: 'Not authorized' }), /user\/password/);
  assert.equal(Sim.mqttErrorReason({ message: 'boom' }), 'boom');
});

// Review #9: hành vi kết nối phụ thuộc phiên bản mqtt.js, và trang giữ credential/JWT — phải ghim bản + SRI.
test('mqtt.js is pinned to an exact version with SRI', () => {
  const tag = html.match(/<script[^>]*mqtt[^>]*\.js[^>]*>/);
  assert.ok(tag, 'không thấy thẻ <script> của mqtt.js');
  assert.match(tag[0], /mqtt@\d+\.\d+\.\d+\//);
  assert.match(tag[0], /integrity="sha384-[A-Za-z0-9+/=]{64}"/);
  assert.match(tag[0], /crossorigin="anonymous"/);
});

// Lệnh bật bị batt_cut chặn phải được báo ra để trang ghi ⚠ (trước đây im lặng -> trông như "đèn hỏng").
test('turn-on commands refused by batt_cut are reported as ignored', () => {
  const s = fresh('auto');
  Sim.applyScenario(s, 'battery-cut');
  const r = Sim.applyControl(s, { pump: true, light: true, mode: 'auto' }, T0);
  assert.deepEqual(r.ignored, ['pump', 'light']);
  assert.equal(r.changed, false);
  assert.deepEqual(Sim.applyControl(s, { pump: false, light: false }, T0).ignored, []); // lệnh tắt không bị chặn
  assert.deepEqual(Sim.applyControl(fresh(), { light: true }, T0).ignored, []);         // pin bình thường
});

test('night during batt_cut flags the blocked auto light (auto mode only)', () => {
  const s = fresh('auto');
  Sim.applyScenario(s, 'battery-cut');
  Sim.applyScenario(s, 'night');
  assert.equal(s.lightOn, false);
  assert.equal(s.autoLightBlocked, true);

  const m = fresh('manual');
  Sim.applyScenario(m, 'battery-cut');
  Sim.applyScenario(m, 'night');
  assert.equal(m.autoLightBlocked, false); // manual: không có ý định bật đèn auto
});

test('configDiff keeps only edited, valid threshold values', () => {
  const diff = Sim.configDiff(Sim.DEFAULT_CONFIG, {
    soil_on_pct: '25', soil_off_pct: '60', lux_on: '', light_auto_pwm: 'abc', pump_cooldown_s: 120, foo: '1',
  });
  assert.deepEqual(diff, { soil_on_pct: 25, pump_cooldown_s: 120 });
});

// Khung "🔔 Thông báo": dựng thẻ từ ĐÚNG payload server gửi Notify (deviceId, event, severity, occurredAt, id, data).
test('notifyView turns server notify payloads into readable cards', () => {
  const at = '2026-10-04T14:04:51Z';
  assert.deepEqual(
    Sim.notifyView({ event: 'battery.low', severity: 'warning', occurredAt: at, data: { batteryPercent: 15 } }),
    { icon: '🪫', title: 'Pin yếu', detail: 'Pin 15%', level: 'warning', at });
  assert.deepEqual(
    Sim.notifyView({ event: 'battery.cut', severity: 'critical', occurredAt: at, data: { batteryPercent: 0, batteryVoltageV: 6.4 } }),
    { icon: '🔌', title: 'Pin cạn — đã ngắt tải', detail: 'Pin 0% · 6.4V', level: 'critical', at });
  assert.equal(Sim.notifyView({ event: 'water.empty', severity: 'critical', data: { soilPercent: 30, waterOk: false } }).detail, 'Đất 30%');
  assert.equal(Sim.notifyView({ event: 'device.offline', severity: 'warning', data: { lastSeenSecondsAgo: 245 } }).detail, 'im lặng 245 s');
  const unknown = Sim.notifyView({ event: 'plant.party', severity: 'info' });
  assert.equal(unknown.title, 'plant.party');
  assert.equal(unknown.level, 'info');
  assert.equal(Sim.notifyView(null).title, 'Thông báo không đọc được');
});

test('unknown scenario returns false', () => {
  assert.equal(Sim.applyScenario(fresh(), 'meteor'), false);
});
