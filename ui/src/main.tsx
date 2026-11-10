// One build, two shells: inside Jason Desktop the console, with the Live chat as its home; in a browser the classic
// shell. Each loads only its own code and its own stylesheet.
const element = document.getElementById('app')!;
const desktop = window.jasonDesktop;
if (desktop) void import('./console/mount').then((shell) => shell.mount(element, desktop));
else void import('./classic').then((shell) => shell.mount(element));
