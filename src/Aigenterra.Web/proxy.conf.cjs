const target = process.env.API_HTTP;
if (!target) throw new Error('API_HTTP is required. Start the web app through Aigenterra.AppHost.');
module.exports = {
  '/api/**': { target, changeOrigin: true, rewrite: (path) => path.replace(/^\/api(?=\/)/, '') },
};
