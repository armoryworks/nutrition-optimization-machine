import { Pipe, PipeTransform } from '@angular/core';

@Pipe({ name: 'memberInitial', standalone: true })
export class MemberInitialPipe implements PipeTransform {
  transform(member: { personName: string }): string {
    return member.personName.charAt(0).toUpperCase();
  }
}
