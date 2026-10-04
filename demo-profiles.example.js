// Profile cho demo-dashboard.html — tab "Kết nối" → ô Profile: chọn 1 profile là trang điền sẵn mọi ô
// (server, tài khoản, thiết bị ảo, MQTT, khung Thông báo), rồi chỉ cần bấm "Đăng nhập".
//
// CÁCH DÙNG
//   1. Copy file này thành  demo-profiles.local.js  (CÙNG thư mục với demo-dashboard.html).
//   2. Điền thông tin thật (mật khẩu tài khoản server, password MQTT của credential planttree-sim).
//   3. Mở lại demo-dashboard.html (Ctrl+F5) → tab Kết nối → chọn profile → Đăng nhập.
//
// demo-profiles.local.js nằm trong .gitignore — KHÔNG commit (có mật khẩu). Mang dashboard sang máy khác
// thì copy kèm file .local.js. File .example.js này chỉ là mẫu, không chứa mật khẩu thật.
window.PLANTTREE_PROFILES = [
  {
    name: 'Test local (dotnet run)',
    server: { host: 'localhost', port: '8000' },              // Host là URL đầy đủ (https://...) thì bỏ Port
    account: { email: 'test@plant-tree.local', password: 'doi-mat-khau' },
    device: { id: 'SIM_Zone1', name: 'Thiết bị ảo Zone 1' },   // bắt buộc tiền tố SIM_
    mqtt: {
      host: 'ba4fbc53bce842ffb0fcd51178d78414.s1.eu.hivemq.cloud',
      user: 'planttree-sim',                                  // phân biệt hoa thường
      password: 'doi-mat-khau',
    },
    notify: true,                                             // bật sẵn khung 🔔 Thông báo
    // mcpUrl: 'http://localhost:8100/mcp',                   // (tuỳ chọn) ô MCP endpoint ở tab Test MCP tools
  },
  {
    name: 'Demo thật (server Mac)',
    server: { host: 'https://api.windy-dev.site' },
    account: { email: 'demo@plant-tree.local', password: 'doi-mat-khau' },
    device: { id: 'SIM_Demo1', name: 'Thiết bị ảo — demo' },  // id KHÁC profile test: 2 server không giẫm nhau
    mqtt: {
      host: 'ba4fbc53bce842ffb0fcd51178d78414.s1.eu.hivemq.cloud',
      user: 'planttree-sim',
      password: 'doi-mat-khau',
    },
    notify: true,
  },
];
