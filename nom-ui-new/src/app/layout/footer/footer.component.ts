import { Component } from '@angular/core';

@Component({
  selector: 'nom-footer',
  imports: [],
  templateUrl: './footer.component.html',
  styleUrl: './footer.component.scss'
})
export class Footer {
  readonly currentYear = new Date().getFullYear();
}
