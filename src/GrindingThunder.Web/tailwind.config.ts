import type { Config } from "tailwindcss";

const config: Config = {
  content: [
    "./app/**/*.{js,ts,jsx,tsx,mdx}",
    "./components/**/*.{js,ts,jsx,tsx,mdx}",
  ],
  theme: {
    extend: {
      colors: {
        canvas: "#070b10",
        panel: "#101720",
        line: "#26313d",
        steel: "#91a2b6",
        signal: "#e7a629",
      },
      boxShadow: {
        panel: "0 24px 70px rgba(0, 0, 0, 0.32)",
      },
    },
  },
  plugins: [],
};

export default config;
