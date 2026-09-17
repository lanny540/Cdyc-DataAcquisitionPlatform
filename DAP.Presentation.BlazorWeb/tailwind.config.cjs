module.exports = {
  content: [
    "./Components/**/*.razor",
    "../DAP.Presentation.BlazorWeb.Client/{Pages,Layout,Common}/**/*.razor"
  ],
  prefix: "tw-",
  corePlugins: { preflight: false },
  theme: {
    extend: {
      colors: {
        primary: "#1565c0",
        surface: "#ffffff",
        canvas: "#f4f6f8"
      }
    }
  }
};
