import { useLocation } from "react-router-dom";
import "./Manager.css";
import DocSpaceFrame from "../components/DocSpaceFrame";

function Manager() {
    const location = useLocation();

    const id = location.state.id;

    const config = {
        mode: "manager",
        showHeader: !id,
        showTitle: true,
        showMenu: !id,
        showFilter: !id,
        id: id
    };

    return (
        <div className="frameHolder">
            <DocSpaceFrame id="zoom-ds-manager-frame" config={config} />
        </div>
    );
  }
  
  export default Manager;
  