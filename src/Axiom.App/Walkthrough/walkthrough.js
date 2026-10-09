'use strict';
const slides = [...document.querySelectorAll('.slide')];
const previous = document.querySelector('#previous');
const next = document.querySelector('#next');
const jump = document.querySelector('#jump');
const counter = document.querySelector('#counter');
const announcement = document.querySelector('#announcement');
const imageLink = document.querySelector('#image-link');
let selected = 0;
function show(index, announce = true) {
    selected = Math.max(0, Math.min(slides.length - 1, index));
    slides.forEach((slide, position) => { slide.hidden = position !== selected; });
    previous.disabled = selected === 0;
    next.disabled = selected === slides.length - 1;
    jump.value = String(selected);
    counter.textContent = `${selected + 1} / ${slides.length}`;
    const image = slides[selected].querySelector('img');
    imageLink.hidden = !image;
    if (image) imageLink.href = image.getAttribute('src');
    if (announce) announcement.textContent = `Step ${selected + 1} of ${slides.length}: ${slides[selected].querySelector('h2').textContent}. ${slides[selected].querySelector('.caption').textContent}`;
}
previous.addEventListener('click', () => show(selected - 1));
next.addEventListener('click', () => show(selected + 1));
jump.addEventListener('change', () => show(Number(jump.value)));
document.addEventListener('keydown', event => {
    if (event.altKey || event.ctrlKey || event.metaKey || event.target instanceof HTMLSelectElement || event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) return;
    const destinations = { ArrowRight: selected + 1, ArrowLeft: selected - 1, Home: 0, End: slides.length - 1 };
    if (!(event.key in destinations)) return;
    event.preventDefault();
    show(destinations[event.key]);
});
document.querySelector('#navigation').hidden = false;
show(0, false);
