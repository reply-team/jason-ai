// The mark and the wordmark "Jason AI, GTM Harness", as images; the wordmark has one file for a light ground and one for a
// dark ground, and the stylesheet shows the one the page's theme calls for.
const base = import.meta.env.BASE_URL;

export function Brand() {
    return (
        <div className="brand">
            <img className="mark" src={`${base}favicon.svg`} alt="" />
            <img className="wordmark light" src={`${base}brand-wordmark.svg`} alt="Jason AI, GTM Harness" />
            <img className="wordmark dark" src={`${base}brand-wordmark-dark.svg`} alt="Jason AI, GTM Harness" />
            <span className="alpha">Alpha</span>
        </div>
    );
}
