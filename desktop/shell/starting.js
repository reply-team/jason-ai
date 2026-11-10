// What the desktop says while the installation comes up, or why it did not; set as text, never as markup.
const query = new URLSearchParams(window.location.search);
const message = query.get('message');
if (message) document.getElementById('message').textContent = message;
if (query.get('state') === 'failed') document.body.classList.add('failed');
