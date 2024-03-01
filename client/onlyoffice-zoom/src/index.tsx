import React from "react";
import ReactDOM from "react-dom/client";
import { createMemoryRouter, RouterProvider } from "react-router-dom";
import "./index.css";
import reportWebVitals from "./reportWebVitals";
import routes from "./routes";

const router = createMemoryRouter(routes);

const root = ReactDOM.createRoot(
  document.getElementById("root") as HTMLElement
);

const isDarkTheme = window.matchMedia("(prefers-color-scheme: dark)").matches;
document.documentElement.dataset.theme = isDarkTheme ? "dark" : "light";

root.render(
  <RouterProvider router={router} />
);

if (process.env!.NODE_ENV === "development") {
  reportWebVitals(console.log);
}
