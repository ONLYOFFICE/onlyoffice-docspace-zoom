import i18n from "i18next";
import { initReactI18next } from "react-i18next";

import de from "./i18n/de.json";
import en from "./i18n/en.json";
import es from "./i18n/es.json";
import fr from "./i18n/fr.json";
import it from "./i18n/it.json";
import ja from "./i18n/ja.json";
import pt_br from "./i18n/pt-BR.json";
import ru from "./i18n/ru.json";
import zh from "./i18n/zh.json";

declare global {
    interface Window {
        _zm_lang:string;
    }
}

const resources = {
    de: {
        translation: de
    },
    en: {
        translation: en
    },
    es: {
        translation: es
    },
    fr: {
        translation: fr
    },
    it: {
        translation: it
    },
    ja: {
        translation: ja
    },
    pt_br: {
        translation: pt_br
    },
    ru: {
        translation: ru
    },
    zh: {
        translation: zh
    },
};

i18n
    .use(initReactI18next)
    .init({
        resources,
        lng: window._zm_lang || navigator.language,
        fallbackLng: "en",

        interpolation: {
            escapeValue: false
        }
    });

export default i18n;