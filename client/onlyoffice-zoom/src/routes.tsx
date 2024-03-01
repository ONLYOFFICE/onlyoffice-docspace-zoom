import App from "./pages/App";
import Landing from "./pages/Landing";
import Manager from "./pages/Manager";
import Meeting from "./pages/Meeting";

const routes = [
    {
        path: "/",
        element: <App />
    },
    {
        path: "/landing",
        element: <Landing />
    },
    {
        path: "/manager",
        element: <Manager />
    },
    {
        path: "/meeting",
        element: <Meeting />
    }
];

export default routes;