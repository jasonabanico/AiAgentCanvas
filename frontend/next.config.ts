import type { NextConfig } from "next";

const isDev = process.env.NODE_ENV === "development";

const nextConfig: NextConfig = {
  ...(isDev ? {} : { output: "export" }),
  distDir: "out",
  async rewrites() {
    return isDev
      ? [
          {
            source: "/api/:path*",
            destination: "http://localhost:5149/api/:path*",
          },
        ]
      : [];
  },
};

export default nextConfig;
